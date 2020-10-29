﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
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

        Mutex mut = new Mutex();
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
        private bool isBusy = false;
        private List<double> lastVals = new List<double>();
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
            timer.Interval = 10;// 250;// 1000;
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
                try
                {
                    this.mut.WaitOne();
                    mlx906.StartDataAcquisition();
                } catch (Exception)
                {

                } finally
                {
                    this.mut.ReleaseMutex();
                }
                timer.Start();
            };

            btnStop.Click += delegate
            {
                timer.Stop();
                try
                {
                    this.mut.WaitOne();
                    mlx906.StopRead();
                    this.lastVals = new List<double>();
                }
                catch (Exception)
                {

                } finally
                {
                    this.mut.ReleaseMutex();
                }
                switchOperation(OPERATION.IDLE);
            };

            btnClear.Click += delegate
            {
                dumpTextView.Text = "";
            };


            btnBackToDeviceList.Click += delegate
            {
                timer.Stop();
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
            if (isBusy)
                return;

            this.mut.WaitOne();
            isBusy = true;
            
            try
            {
                short[] raw_frame = mlx906.ReadFrame();
                //if (raw_frame != null)
                if(false)
                {
                    double[] frame;
                    double Tamb;
                    (frame, Tamb) = mlx906.DoCompensation(raw_frame);
                    frame = frame.Select(v => Math.Round(v, 2)).ToArray();
                    double max = frame.Max();

                    this.lastVals.Add(max);
                    if (this.lastVals.Count > 4)
                        this.lastVals.RemoveAt(0);

                    double average = Enumerable.Average(lastVals);
                    string str = String.Format("TA={0:0.00}, Max={1:0.00}, AvgLast4={2:0.00}", Tamb, max, average);
                    string message = String.Format("{0:HH:mm:ss tt}: {1}\n", DateTime.Now, str);
                    //string joined = String.Format("{0} {1}\n", message, string.Join(", ", frame));
                    messageCount++;

                    RunOnUiThread(() =>
                    {
                        tvLatest.Text = str;
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
                            folder.Dispose();
                        }
                        
                        if (sdCardPath != null)
                        {
                            var filePath = System.IO.Path.Combine(sdCardPath, "Temperature_Melexis.csv");
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
            catch (Exception ex)
            {
                //titleTextView.Text = "Error read frame: " + ex.Message;
                Log.Error(TAG, "Error read frame: + ", ex.Message);
                //try
                //{
                //    mlx906.ClearError();
                //} catch (Exception)
                //{

                //}
                
            }
            isBusy = false;
            this.mut.ReleaseMutex();
            //timer.Start();
            
        }

        protected override void OnPause()
        {
            Log.Info(TAG, "OnPause");

            base.OnPause();

            timer.Stop();
            try
            {
                this.mut.WaitOne();
                mlx906.StopRead();
                this.lastVals = new List<double>();
            }
            catch (Exception)
            {

            }
            finally
            {
                this.mut.ReleaseMutex();
            }

            if (serialIoManager != null && serialIoManager.IsOpen)
            {
                Log.Info(TAG, "Stopping IO manager ..");
                try
                {
                    serialIoManager.Close();
                    serialIoManager.Dispose();
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
            portInfo.Dispose(); //richard: dispose
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

            bool success = port.PurgeHwBuffers(false, false);

            serialIoManager = new SerialInputOutputManager(port)
            {
                BaudRate = 38400,// 57600,
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
                this.mut.WaitOne();
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
                //return;
            }
            finally
            {
                this.mut.ReleaseMutex();
            }

            
        }


        
    }
}