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
        MLX906 mlx906;

        TextView titleTextView;
        TextView dumpTextView;
        ScrollView scrollView;
        TextView tvLatest;

        Button btnStart;
        Button btnStop;
        Button btnClear;

        Button btnBackToDeviceList;

        enum OPERATION
        {
            IDLE,
            INIT,
            READ,
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
            
            btnStart.Click += delegate
            {
                switchOperation(OPERATION.READ);
                timer.Start();
            };

            btnStop.Click += delegate
            {
                timer.Stop();
                try
                {
                    mlx906.StopRead();
                }
                catch (Exception)
                {

                }
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
        }

        void switchOperation(OPERATION op)
        {
            if(op == OPERATION.IDLE)
            {
                btnStart.Enabled = true;
                btnStop.Enabled = false;
            }
            else
            {
                btnStart.Enabled = false;
                btnStop.Enabled = true;
            }
            mOperation = op;
        }

        private void OnTimerEvent(object sender, System.Timers.ElapsedEventArgs e)
        {
            //WriteData(getdata);
            //timer.Stop();
            try
            {
                short[] raw_frame = mlx906.ReadFrame();
                if (raw_frame != null)
                {
                    double[] frame;
                    double Tamb;
                    (frame, Tamb) = mlx906.DoCompensation(raw_frame);
                    frame = frame.Select(v => Math.Round(v, 2)).ToArray();
                    string max = String.Format("TA={0:0.00}, MAX={1:0.00}", Tamb, frame.Max());
                    string message = String.Format("{0:HH:mm:ss tt}: {1}\n", DateTime.Now, max);
                    string joined = String.Format("{0} {1}\n", message, string.Join(", ", frame));
                    messageCount++;

                    RunOnUiThread(() =>
                    {
                        tvLatest.Text = max;
                        if (messageCount == 200)
                        {
                            messageCount = 0;
                            dumpTextView.Text = "";
                        }
                        dumpTextView.Append(message);
                        scrollView.SmoothScrollTo(0, dumpTextView.Bottom);
                    });
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
                            var filePath = System.IO.Path.Combine(sdCardPath, "Temperature_Melexis.csv");
                            if (Directory.Exists(sdCardPath))
                            {
                                var fs = new FileStream(filePath, FileMode.Append);
                                byte[] txt = new UTF8Encoding(true).GetBytes(joined);
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
            catch (Exception ex)
            {
                //titleTextView.Text = "Error read frame: " + ex.Message;
                Log.Error(TAG, "Error read frame: + ", ex.Message);
                try
                {
                    mlx906.ClearError();
                } catch (Exception)
                {

                }
                
            }
            //timer.Start();
            
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
                catch (Exception)
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
                mlx906 = null;
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
            serialIoManager.DataReceived += (sender, e) =>
            {
                MLX906.pushData(e.Data);
                //Log.Info(TAG, "DataReceived");
                /*RunOnUiThread(() => {
                    UpdateReceivedData(e.Data);
                });*/
            };
            serialIoManager.ErrorReceived += (sender, e) =>
            {
                RunOnUiThread(async () =>
                {
                    await Task.Delay(1000);
                    var intent = new Intent(this, typeof(MainActivity));
                    StartActivity(intent);
                });
            };

            //Log.Info(TAG, "Starting IO manager ..");
            switchOperation(OPERATION.INIT);
            try
            {
                serialIoManager.Open(usbManager);
                mlx906 = new MLX906(port);
                mlx906.Init();
                btnStart.PerformClick();
            }
            catch (Exception e)
            {
                titleTextView.Text = "Error opening device: " + e.Message;
                switchOperation(OPERATION.IDLE);
                mlx906 = null;
                RunOnUiThread(async () => {
                    await Task.Delay(1000);
                    var intent = new Intent(this, typeof(MainActivity));
                    StartActivity(intent);
                });
                return;
            }

            
        }


        
    }
}