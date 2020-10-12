using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Hardware.Usb;
using Android.OS;
using Android.Runtime;
using Android.Util;
using Android.Views;
using Android.Views.InputMethods;
using Android.Widget;

using Hoho.Android.UsbSerial.Driver;
using Hoho.Android.UsbSerial.Extensions;
using Hoho.Android.UsbSerial.Util;


namespace TempModTest_MLX906
{
    [Activity(Label = "@string/app_name", LaunchMode = LaunchMode.SingleTop)]
    public class TempModTestActivity : Activity
    {
        static readonly string TAG = typeof(TempModTestActivity).Name;

        public const string EXTRA_TAG = "PortInfo";
        const int READ_WAIT_MILLIS = 200;
        const int WRITE_WAIT_MILLIS = 200;
        const short EEPROM_SIZE = 192;
        const short ADDR_TB_CORR = 440;
        const short LEN_TB_CORR = 8;

        UsbSerialPort port;

        UsbManager usbManager;
        TextView titleTextView;
        TextView dumpTextView;
        ScrollView scrollView;
        TextView tvLatest;

        Button btnStart;
        Button btnStop;
        Button btnClear;

        Button btnBackToDeviceList;
        Button btnSaveTBCorrection;

        EditText editTBOffset;

        enum OPERATION
        {
            IDLE,
            INIT,
            READ,
            LOADEEPROM,
            SAVEEEPROM,
            SAVE_TB_CORRECTION,
            SAVE_TB_CORRECTION2,
        };

        OPERATION mOperation;

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
            timer.Interval = 250;// 1000;
            timer.Stop();

            usbManager = GetSystemService(Context.UsbService) as UsbManager;
            titleTextView = FindViewById<TextView>(Resource.Id.demoTitle);
            dumpTextView = FindViewById<TextView>(Resource.Id.consoleText);
            scrollView = FindViewById<ScrollView>(Resource.Id.demoScroller);
            tvLatest = FindViewById<TextView>(Resource.Id.tvLatest);

            btnStart = FindViewById<Button>(Resource.Id.start);
            btnStop = FindViewById<Button>(Resource.Id.stop);
            btnClear = FindViewById<Button>(Resource.Id.clear);
            btnBackToDeviceList = FindViewById<Button>(Resource.Id.backToDeviceList);
            btnSaveTBCorrection = FindViewById<Button>(Resource.Id.saveTBCorrection);
            editTBOffset = FindViewById<EditText>(Resource.Id.editTBOffset);

            editTBOffset.FocusChange += delegate
            {
                InputMethodManager inputManager = (InputMethodManager)GetSystemService(Context.InputMethodService);
                inputManager.HideSoftInputFromWindow(editTBOffset.WindowToken, HideSoftInputFlags.None);
            };

            btnStart.Click += delegate
            {
                switchOperation(OPERATION.READ);
                timer.Start();
            };

            btnStop.Click += delegate
            {
                timer.Stop();
                switchOperation(OPERATION.IDLE);
            };

            btnClear.Click += delegate
            {
                dumpTextView.Text = "";
            };


            btnBackToDeviceList.Click += delegate
            {
                var intent = new Intent(this, typeof(MainActivity));
                StartActivity(intent);
            };

            btnSaveTBCorrection.Click += delegate
            {
                double val = Double.Parse(editTBOffset.Text);
                if(val > 12.7 || val < -12.8)
                {
                    Toast.MakeText(Application.Context, "TB Offset over range(-12.8 to 12.7)", ToastLength.Long).Show();
                    return;
                }                
                
                byte[] tbdata = new byte[6];
                tbdata[0] = 0xE1;
                tbdata[1] = 0x01;
                tbdata[2] = 0xB3;
                tbdata[3] = 0x00;
                tbdata[4] = 0x01;
                tbdata[5] = (byte)(int)(val * 10);

                if (serialIoManager.IsOpen)
                {
                    switchOperation(OPERATION.SAVE_TB_CORRECTION);
                    port.Write(tbdata, WRITE_WAIT_MILLIS);
                }
            };
        }

        void switchOperation(OPERATION op)
        {
            if(op == OPERATION.IDLE)
            {
                btnStart.Enabled = true;
                //btnStop.Enabled = true;
                btnSaveTBCorrection.Enabled = true;
            }
            else
            {
                btnStart.Enabled = false;
                //btnStop.Enabled = true;
                btnSaveTBCorrection.Enabled = false;
            }
            mOperation = op;
        }

        byte[] getdata = new byte[] { 0xad, 0x00, 0x23, 0x14, 0x4c };

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
                RunOnUiThread(async () => {
                    await Task.Delay(1000);
                    var intent = new Intent(this, typeof(MainActivity));
                    StartActivity(intent);
                });
            };

            Log.Info(TAG, "Starting IO manager ..");
            try
            {
                serialIoManager.Open(usbManager);
                btnStart.PerformClick();
            }
            catch (Exception e)
            {
                titleTextView.Text = "Error opening device: " + e.Message;
                RunOnUiThread(async () => {
                    await Task.Delay(1000);
                    var intent = new Intent(this, typeof(MainActivity));
                    StartActivity(intent);
                });
                return;
            }
        }

        void WriteData(byte[] data)
        {
            if (serialIoManager.IsOpen)
            {
                try
                {
                    port.Write(data, WRITE_WAIT_MILLIS);
                }
                catch (Exception e)
                {
                    titleTextView.Text = "Error Write Data: " + e.Message;
                    RunOnUiThread(async () => {
                        await Task.Delay(1000);
                        var intent = new Intent(this, typeof(MainActivity));
                        StartActivity(intent);
                    });
                    return;
                    }
                }
        }

        void UpdateReceivedData(byte[] data)
        {
            if (mOperation == OPERATION.SAVE_TB_CORRECTION)
            {
                byte[] tbdata = new byte[7];
                tbdata[0] = 0xE1;
                tbdata[1] = 0x01;
                tbdata[2] = 0xB3;
                tbdata[3] = 0x00;
                tbdata[4] = 0x01;
                tbdata[5] = 0xAA;
                tbdata[6] = 0x55;

                if (serialIoManager.IsOpen)
                {
                    switchOperation(OPERATION.SAVE_TB_CORRECTION2);
                    port.Write(tbdata, WRITE_WAIT_MILLIS);
                }
                else
                {
                    Toast.MakeText(Application.Context, "Device disconnected", ToastLength.Long).Show();
                    switchOperation(OPERATION.IDLE);
                }
            }
            else if (mOperation == OPERATION.SAVE_TB_CORRECTION2)
            {
                Toast.MakeText(Application.Context, "TB Offset written to EEPROM", ToastLength.Long).Show();
                switchOperation(OPERATION.IDLE);
            }
            else if (mOperation == OPERATION.READ)
            { 
                //handle read data
                if (data.Length < 2)
                {
                    return;
                }
                if (data[0] == 0xaa && data[1] == 0x23)
                {
                    if (data[2] == 0xff && data[3] == 0xff)
                    {
                        return;
                    }
                    processFrame(data);
                }
            }
            
            return;
        }

        void processFrame(byte[] data)
        {
            int index = 2;
            int count = 17;
            string message = String.Format("{0:HH:mm:ss tt}, ", DateTime.Now);
            string latestMsg = message;

            for(int i = 0; i < count; ++i, index+=2)
            {
                double value = (data[index+1] * 256 + data[index]) / 10.0;
                message += String.Format("{0:0.00}, ", value);
                if(i % 4 == 1)
                {
                    latestMsg += "\n";
                }
                latestMsg += String.Format("{0:0.00}, ", value);
            }
            message += "\n";
            messageCount++;

            if (messageCount == 200)
            {
                messageCount = 0;
                dumpTextView.Text = "";
            }
            dumpTextView.Append(message);
            //scrollView.SmoothScrollTo(0, dumpTextView.Bottom);
            tvLatest.Text = latestMsg;
            Log.Info(TAG, message);

            try
            {
                Context context = Application.Context;
                Java.IO.File[] dirs = context.GetExternalFilesDirs(null);
                string sdCardPath = null;
                foreach (Java.IO.File folder in dirs)
                {
                    bool isRemovable = Android.OS.Environment.InvokeIsExternalStorageRemovable(folder);
                    bool isEmulated = Android.OS.Environment.InvokeIsExternalStorageEmulated(folder);

                    if (isRemovable && !isEmulated)
                    {
                        sdCardPath = folder.Path.Split("/Android")[0];
                        break;
                    }
                }
                if (sdCardPath != null)
                {
                    var filePath = System.IO.Path.Combine(sdCardPath, "Temperature_Omron.csv");
                    if (Directory.Exists(sdCardPath))
                    {
                        var fs = new FileStream(filePath, FileMode.Append);
                        byte[] txt = new UTF8Encoding(true).GetBytes(message);
                        fs.Write(txt, 0, txt.Length);
                        fs.Close();
                    }
                }
            }
            catch
            {

            }
        }

        
    }
}