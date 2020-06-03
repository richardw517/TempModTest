using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Hardware.Usb;
using Android.OS;
using Android.Runtime;
using Android.Util;
using Android.Views;
using Android.Widget;

using Hoho.Android.UsbSerial.Driver;
using Hoho.Android.UsbSerial.Extensions;
using Hoho.Android.UsbSerial.Util;


namespace TempModTest
{
    [Activity(Label = "@string/app_name", LaunchMode = LaunchMode.SingleTop)]
    public class TempModTestActivity : Activity
    {
        static readonly string TAG = typeof(TempModTestActivity).Name;

        public const string EXTRA_TAG = "PortInfo";
        const int READ_WAIT_MILLIS = 200;
        const int WRITE_WAIT_MILLIS = 200;

        UsbSerialPort port;

        UsbManager usbManager;
        TextView titleTextView;
        TextView dumpTextView;
        ScrollView scrollView;
        TextView tvLatest;

        Button btnStart;
        Button btnStop;
        Button btnClear;

        SerialInputOutputManager serialIoManager;
        private System.Timers.Timer timer = null;
        int messageCount = 0;

        protected override void OnCreate(Bundle savedInstanceState)
        {
            Log.Info(TAG, "OnCreate");

            base.OnCreate(savedInstanceState);

            SetContentView(Resource.Layout.TempModTest);
            timer = new System.Timers.Timer();
            timer.Elapsed += OnTimerEvent;
            timer.Enabled = true;
            timer.AutoReset = true;
            timer.Interval = 1000;
            timer.Stop();

            usbManager = GetSystemService(Context.UsbService) as UsbManager;
            titleTextView = FindViewById<TextView>(Resource.Id.demoTitle);
            dumpTextView = FindViewById<TextView>(Resource.Id.consoleText);
            scrollView = FindViewById<ScrollView>(Resource.Id.demoScroller);
            tvLatest = FindViewById<TextView>(Resource.Id.tvLatest);

            btnStart = FindViewById<Button>(Resource.Id.start);
            btnStop = FindViewById<Button>(Resource.Id.stop);
            btnClear = FindViewById<Button>(Resource.Id.clear);

            btnStart.Click += delegate
            {
                timer.Start();
            };

            btnStop.Click += delegate
            {
                timer.Stop();
            };

            btnClear.Click += delegate
            {
                dumpTextView.Text = "";
            };
        }

        byte[] getdata = new byte[] { 0xad, 0x02, 0x0d, 0xd0, 0x4e };

        private void OnTimerEvent(object sender, System.Timers.ElapsedEventArgs e)
        {
            WriteData(getdata);
        }

        protected override void OnPause()
        {
            Log.Info(TAG, "OnPause");

            base.OnPause();

            if (serialIoManager != null && serialIoManager.IsOpen)
            {
                Log.Info(TAG, "Stopping IO manager ..");
                try
                {
                    serialIoManager.Close();
                }
                catch (Java.IO.IOException)
                {
                    // ignore
                }
            }
        }

        protected async override void OnResume()
        {
            Log.Info(TAG, "OnResume");

            base.OnResume();

            var portInfo = Intent.GetParcelableExtra(EXTRA_TAG) as UsbSerialPortInfo;
            int vendorId = portInfo.VendorId;
            int deviceId = portInfo.DeviceId;
            int portNumber = portInfo.PortNumber;

            Log.Info(TAG, string.Format("VendorId: {0} DeviceId: {1} PortNumber: {2}", vendorId, deviceId, portNumber));

            var drivers = await MainActivity.FindAllDriversAsync(usbManager);
            var driver = drivers.Where((d) => d.Device.VendorId == vendorId && d.Device.DeviceId == deviceId).FirstOrDefault();
            if (driver == null)
                throw new Exception("Driver specified in extra tag not found.");

            port = driver.Ports[portNumber];
            if (port == null)
            {
                titleTextView.Text = "No serial device.";
                return;
            }
            Log.Info(TAG, "port=" + port);

            titleTextView.Text = "Serial device: " + port.GetType().Name;

            serialIoManager = new SerialInputOutputManager(port)
            {
                BaudRate = 57600,
                DataBits = 8,
                StopBits = StopBits.One,
                Parity = Parity.None,
            };
            serialIoManager.DataReceived += (sender, e) => {
                RunOnUiThread(() => {
                    UpdateReceivedData(e.Data);
                });
            };
            serialIoManager.ErrorReceived += (sender, e) => {
                RunOnUiThread(() => {
                    var intent = new Intent(this, typeof(MainActivity));
                    StartActivity(intent);
                });
            };

            Log.Info(TAG, "Starting IO manager ..");
            try
            {
                serialIoManager.Open(usbManager);
            }
            catch (Java.IO.IOException e)
            {
                titleTextView.Text = "Error opening device: " + e.Message;
                return;
            }
        }

        void WriteData(byte[] data)
        {
            if (serialIoManager.IsOpen)
            {
                port.Write(data, WRITE_WAIT_MILLIS);
            }
        }

        byte[] frame = new byte[576];
        int writeIndex = 0;

        void UpdateReceivedData(byte[] data)
        {
            if (data.Length < 2)
            {
                writeIndex = 0;
                return;
            }
            int readFrom = 0;
            while (readFrom < data.Length)
            {
                if (data[readFrom] != 0xAA && data[readFrom] != 0xAB)
                {
                    writeIndex = 0;
                    return;
                }
                int length = data[readFrom + 1];
                if (length > 62 || writeIndex + length > frame.Length)
                {
                    writeIndex = 0;
                    return;
                }

                Buffer.BlockCopy(data, readFrom + 2, frame, writeIndex, length);
                if (data[readFrom] == 0xAA)
                {
                    writeIndex = 0;
                    processFrame();
                }
                else
                    writeIndex += length;
                readFrom += 64;
            }
            return;

        }

        double data2Temp(int data)
        {
            return (data - 27315) / 100.0;
        }

        void processFrame()
        {
            int ambient = frame[9] * 256 + frame[10];
            int startLine = 13 + 2 * 16 * 5; //skip 5 lines
            int maxCenter = 0;
            for (int j = 0; j < 6; j++, startLine += 2 * 16)
            {
                for (int i = 0, index = startLine + 2 * 5; i < 6; i++, index += 2)
                {
                    int data = frame[index] * 256 + frame[index + 1];
                    if (data > maxCenter)
                        maxCenter = data;
                }
            }
            double ambientTemp = data2Temp(ambient);
            double maxCenterTemp = data2Temp(maxCenter);
            double adjustedTemp = adjustTemp(maxCenterTemp, ambientTemp);

            string message = String.Format("{0:0.00}, {1:0.00}, {2:0.00}\n", ambientTemp, maxCenterTemp, adjustedTemp);
            messageCount++;
            if (messageCount == 200)
            {
                messageCount = 0;
                dumpTextView.Text = "";
            }
            dumpTextView.Append(message);
            //scrollView.SmoothScrollTo(0, dumpTextView.Bottom);
            tvLatest.Text = message;
            Log.Info(TAG, message);
        }

        double adjustTemp(double InValue, double TA)
        {
            double tmp0 = 0;
            double tmp1 = 0;
            do
            {
                if (InValue < 32.5)
                {
                    tmp0 = (273.0 / 256.0) * InValue + 1.68;
                    break;
                }

                if (InValue < 34.5)
                {
                    tmp0 = (57.0 / 256.0) * InValue + 29.13;
                    break;
                }

                if (InValue > 34.5)
                {
                    tmp0 = (223.0 / 256.0) * InValue + 6.58;
                    break;
                }
            } while (false);

            do
            {

                if (TA < 26.8)
                {
                    tmp1 = (35.0 - TA) / 3.0 * 0.22;
                    break;
                }

                if (TA < 27.5)
                {
                    tmp1 = (30.0 - TA) / 2.0 * 0.15;
                    break;
                }

            } while (false);


            return tmp1 + tmp0;
        }
    }
}