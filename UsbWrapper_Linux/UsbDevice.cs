﻿using System;
using System.Collections.Generic;
using System.Text;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;
using System.IO;

namespace Pololu.UsbWrapper
{
    /// <summary>
    /// A class that represents a device connected to the computer.  This
    /// class can be used as an item in the device list dropdown box.
    /// </summary>
    public class DeviceListItem
    {
        String privateText;
            
        /// <summary>
        /// Gets the text to display to the user in the list to represent this
        /// device.  Typically text is "#" + serialNumberString.
        /// </summary>
        public String text
        {
            get
            {
                return privateText;
            }
        }

        String privateSerialNumber;

        /// <summary>
        /// Gets the serial number.
        /// </summary>
        public String serialNumber
        {
            get
            {
                return privateSerialNumber;
            }
        }

        IntPtr privateDevicePointer;

        /// <summary>
        /// Gets the product Id
        /// </summary>
        internal IntPtr devicePointer
        {
            get
            {
                return privateDevicePointer;
            }
        }

        /// <summary>
        /// true if the devices are the same
        /// </summary>
        /// <param name="item"></param>
        /// <returns></returns>
        public bool isSameDeviceAs(DeviceListItem item)
        {
            return (devicePointer == item.devicePointer);
        }

        /// <summary>
        /// Creates an item that doesn't actually refer to a device; just for populating the list with things like "Disconnected"
        /// </summary>
        /// <param name="text"></param>
        public static DeviceListItem CreateDummyItem(String text)
        {
            var item = new DeviceListItem(IntPtr.Zero,text,"");
            return item;
        }

        internal DeviceListItem(IntPtr devicePointer, string text, string serialNumber)
        {
            privateDevicePointer = devicePointer;
            privateText = text;
            privateSerialNumber = serialNumber;
        }

        ~DeviceListItem()
        {
            if(privateDevicePointer != IntPtr.Zero)
                UsbDevice.libusbUnrefDevice(privateDevicePointer);
        }
    }

    internal class LibusbContext : SafeHandle
    {
        private LibusbContext() : base(IntPtr.Zero,true)
        {
        }

        public override bool IsInvalid
        {
            get
            {
                return (handle == IntPtr.Zero);
            }
        }

        [DllImport("libusb-1.0", EntryPoint = "libusb_exit")]
        /// <summary>
        /// called with the context when closing
        /// </summary>
        static extern void libusbExit(IntPtr ctx);

        override protected bool ReleaseHandle()
        {
            libusbExit(handle);
            return true;
        }
    }

    public static class Usb
    {
        public static int WM_DEVICECHANGE { get { return 0; } }        

        public static bool supportsNotify { get { return false; } }

        public static IntPtr notificationRegister(Guid guid, IntPtr handle)
        {
            throw new NotSupportedException();
        }

        /// <summary>
        /// Returns a list of port names (e.g. "COM2", "COM3") for all
        /// ACM USB serial ports.  Ignores the deviceInstanceIdPrefix argument. 
        /// </param>
        /// <returns></returns>
        public static IList<String> getPortNames(String deviceInstanceIdPrefix)
        {
            IList<String> l = new List<String>();
            foreach(string s in Directory.GetFiles("/dev/"))
            {
                if(s.StartsWith("/dev/ttyACM") || s.StartsWith("/dev/ttyUSB"))
                    l.Add(s);
            }
            return l;
        }
    }

    internal static class LibUsb
    {
        /// <summary>
        /// Raises an exception if its argument is negative, with a
        /// message describing which LIBUSB_ERROR it is.
        /// </summary>
        /// <returns>the code, if it is non-negative</returns>
        internal static int throwIfError(int code)
        {
            if(code >= 0)
                return code;

            throw new Exception(LibUsb.errorDescription(code));
        }

        /// <summary>
        /// Raises an exception if its argument is negative, with a
        /// message prefixed by the message parameter and describing
        /// which LIBUSB_ERROR it is.
        /// </summary>
        /// <returns>the code, if it is non-negative</returns>
        internal static int throwIfError(int code, string message)
        {
            try
            {
                return LibUsb.throwIfError(code);
            }
            catch(Exception e)
            {
                throw new Exception(message, e);
            }
        }

        internal static string errorDescription(int error)
        {
            switch(error)
            {
            case -1:
                return "I/O error";
            case -2:
                return "invalid parameter";
            case -3:
                return "access denied";
            case -4:
                return "device does not exist";
            case -5:
                return "no such entity";
            case -6:
                return "busy";
            case -7:
                return "timeout";
            case -8:
                return "overflow";
            case -9:
                return "pipe error";
            case -10:
                return "system call was interrupted";
            case -11:
                return "out of memory";
            case -12:
                return "unsupported/unimplemented operation";
            case -99:
                return "other error";
            default:
                return "unknown error or not an error?";
            };
        }

        /// <summary>
        /// Do not use directly.  The property below initializes this
        /// with libusbInit when it is first used.
        /// </summary>
        private static LibusbContext privateContext;

        internal static LibusbContext context
        {
            get
            {
                if(privateContext == null || privateContext.IsInvalid)
                {
                    LibUsb.throwIfError(UsbDevice.libusbInit(out privateContext));
                }
                return privateContext;
            }
        }

        /// <returns>the serial number</returns>
        internal static unsafe string getSerialNumber(IntPtr device_handle)
        {
            LibusbDeviceDescriptor descriptor = getDeviceDescriptor(device_handle);
            byte[] buffer = new byte[18];
            int length;
            fixed(byte* p = buffer)
            {
                length = LibUsb.throwIfError(UsbDevice.libusbGetStringDescriptorASCII(device_handle, descriptor.iSerialNumber, p, 18));
            }
            int i;
            String serial_number = "";
            for(i=0;i<length;i++)
            {
                serial_number += (char)buffer[i];
            }
            return serial_number;
        }

        /// <returns>true iff the vendor and product ids match the device</returns>
        internal static bool deviceMatchesVendorProduct(IntPtr device, ushort idVendor, ushort idProduct)
        {
            LibusbDeviceDescriptor descriptor = getDeviceDescriptorFromDevice(device);
            return idVendor == descriptor.idVendor && idProduct == descriptor.idProduct;
        }

        /// <returns>the device descriptor</returns>
        internal static LibusbDeviceDescriptor getDeviceDescriptor(IntPtr device_handle)
        {
            return getDeviceDescriptorFromDevice(UsbDevice.libusbGetDevice(device_handle));
        }
        
        /// <returns>the device descriptor</returns>
        static LibusbDeviceDescriptor getDeviceDescriptorFromDevice(IntPtr device)
        {
            LibusbDeviceDescriptor descriptor;
            LibUsb.throwIfError(UsbDevice.libusbGetDeviceDescriptor(device, out descriptor),
                               "Failed to get device descriptor");
            return descriptor;
        }
    }

    public abstract class UsbDevice
    {
        protected string getProductID()
        {
            return LibUsb.getDeviceDescriptor(deviceHandle).idProduct.ToString("X4");
        }

        /// <summary>
        /// Gets the serial number.
        /// </summary>
        public String getSerialNumber()
        {
            return LibUsb.getSerialNumber(deviceHandle);
        }


        protected void controlTransfer(byte RequestType, byte Request, ushort Value, ushort Index)
        {
            int ret;
            ret = libusbControlTransfer(deviceHandle, RequestType, Request,
                                        Value, Index, new byte[] { }, 0, 5000);
            if(ret != 0)
            {
                throw new Exception("Control transfer failed.");
            }
        }

        protected void controlTransfer(byte RequestType, byte Request, ushort Value, ushort Index, byte[] data)
        {
            int ret;
            ret = libusbControlTransfer(deviceHandle, RequestType, Request,
                                        Value, Index, data, (ushort)data.Length, (ushort)5000);

            LibUsb.throwIfError(ret,"Control transfer failed");
        }

        IntPtr deviceHandle;

        /// <summary>
        /// Create a usb device from a deviceListItem
        /// </summary>
        /// <param name="handles"></param>
        protected UsbDevice(DeviceListItem deviceListItem)
        {
            LibUsb.throwIfError(libusbOpen(deviceListItem.devicePointer,out deviceHandle),
                               "Error connecting to device.");
        }

        /// <summary>
        /// disconnects from the usb device
        /// </summary>
        public void disconnect()
        {
            libusbClose(deviceHandle);
        }

        [DllImport("libusb-1.0", EntryPoint = "libusb_control_transfer")]
        /// <returns>the number of bytes transferred or an error code</returns>
        static extern int libusbControlTransfer(IntPtr device_handle, byte requesttype,
                                                byte request, ushort value, ushort index,
                                                byte[] bytes, ushort size, uint timeout);

        [DllImport("libusb-1.0", EntryPoint = "libusb_get_device_descriptor")]
        internal static extern int libusbGetDeviceDescriptor(IntPtr device, out LibusbDeviceDescriptor device_descriptor);

        [DllImport("libusb-1.0", EntryPoint = "libusb_init")]
        /// <summary>
        /// called to initialize the device context before any using any libusb functions
        /// </summary>
        /// <returns>an error code</returns>
        internal static extern int libusbInit(out LibusbContext ctx);

        [DllImport("libusb-1.0", EntryPoint = "libusb_get_device_list")]
        /// <summary>
        /// gets a list of device pointers - must be freed with libusbFreeDeviceList
        /// </summary>
        /// <returns>number of devices OR an error code</returns>
        internal static unsafe extern int libusbGetDeviceList(LibusbContext ctx, out IntPtr* list);

        [DllImport("libusb-1.0", EntryPoint = "libusb_free_device_list")]
        /// <summary>
        /// Frees a device list.  Decrements the reference count for each device by 1
        /// if the unref_devices parameter is set.
        /// </summary>
        internal static unsafe extern void libusbFreeDeviceList(IntPtr* list, int unref_devices);

        [DllImport("libusb-1.0", EntryPoint = "libusb_unref_device")]
        /// <summary>
        /// Decrements the reference count on a device.
        /// </summary>
        internal static extern void libusbUnrefDevice(IntPtr device);

        [DllImport("libusb-1.0", EntryPoint = "libusb_get_string_descriptor_ascii")]
        /// <summary>
        /// Gets the simplest version of a string descriptor
        /// </summary>
        internal static unsafe extern int libusbGetStringDescriptorASCII(IntPtr device_handle, byte index, byte *data, int length);

        [DllImport("libusb-1.0", EntryPoint = "libusb_open")]
        /// <summary>
        /// Gets a device handle for a device.  Must be closed with libusb_close.
        /// </summary>
        internal static extern int libusbOpen(IntPtr device, out IntPtr device_handle);

        [DllImport("libusb-1.0", EntryPoint = "libusb_close")]
        /// <summary>
        /// Closes a device handle.
        /// </summary>
        internal static extern void libusbClose(IntPtr device_handle);

        [DllImport("libusb-1.0", EntryPoint = "libusb_get_device")]
        /// <summary>
        /// Gets the device from a device handle.
        /// </summary>
        internal static extern IntPtr libusbGetDevice(IntPtr device_handle);

        /// <summary>
        /// true if the devices are the same
        /// </summary>
        /// <param name="item"></param>
        /// <returns></returns>
        public bool isSameDeviceAs(DeviceListItem item)
        {
            return (libusbGetDevice(deviceHandle) == item.devicePointer);
        }

        /// <summary>
        /// gets a list of devices
        /// </summary>
        /// <returns></returns>
        protected static unsafe List<DeviceListItem> getDeviceList(Guid deviceInterfaceGuid)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// gets a list of devices by vendor and product ID
        /// </summary>
        /// <returns></returns>
        protected static unsafe List<DeviceListItem> getDeviceList(UInt16 vendorId, UInt16[] productIdArray)
        {
            var list = new List<DeviceListItem>();

            IntPtr* device_list;
            int count = LibUsb.throwIfError(UsbDevice.libusbGetDeviceList(LibUsb.context,out device_list));
            int i;
            for(i=0;i<count;i++)
            {
                IntPtr device = device_list[i];

                foreach(UInt16 productId in productIdArray)
                {
                    if(LibUsb.deviceMatchesVendorProduct(device, vendorId, productId))
                    {
                        IntPtr device_handle;
                        LibUsb.throwIfError(UsbDevice.libusbOpen(device,out device_handle),
                                           "Error connecting to device.");

                        string s = "#" + LibUsb.getSerialNumber(device_handle);
                        list.Add(new DeviceListItem(device,s,s.Substring(1)));

                        UsbDevice.libusbClose(device_handle);
                    }
                }
            }

            // Free device list without unreferencing.
            // Unreference/free the individual devices in the
            // DeviceListItem destructor.
            UsbDevice.libusbFreeDeviceList(device_list, 0);

            return list;
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack=1)]
    internal struct LibusbDeviceDescriptor
    {
        public byte   bLength;
        public byte   bDescriptorType;
        public ushort bcdUSB;
        public byte   bDeviceClass;
        public byte   bDeviceSubClass;
        public byte   bDeviceProtocol;
        public byte   bMaxPacketSize0;
        public ushort idVendor;
        public ushort idProduct;
        public ushort bcdDevice;
        public byte   iManufacturer;
        public byte   iProduct;
        public byte   iSerialNumber;
        public byte   bNumConfigurations;
    };
}

// Local Variables: **
// mode: java **
// c-basic-offset: 4 **
// tab-width: 4 **
// indent-tabs-mode: nil **
// end: **
