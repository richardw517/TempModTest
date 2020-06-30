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

        byte[] init0data = new byte[] { 0xAC, 0xD0, 0x2F, 0x04, 0x00 };
        byte[] initdata = new byte[] { 0xAD, 0x00, 0x02, 0xD0, 0xB0, 0x10 };
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
                WriteData(init0data);
                await Task.Delay(200);
                WriteData(initdata);
                await Task.Delay(200);
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
                    if (writeIndex + length > 64)
                        processFrame();
                    writeIndex = 0;
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

            string message = String.Format("{0:0.00}, {1:0.00}, {2:0.00}, {3:HH:mm:ss tt}\n", ambientTemp, maxCenterTemp, adjustedTemp, DateTime.Now);
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
                    var filePath = System.IO.Path.Combine(sdCardPath, "Temperature.csv");
                    if (Directory.Exists(sdCardPath))
                    {
                        var fs = new FileStream(filePath, FileMode.Append);
                        byte[] txt = new UTF8Encoding(true).GetBytes(message);
                        fs.Write(txt, 0, txt.Length);
                        fs.Close();
                    }
                }
            } catch
            {

            }
        }

        struct FormulaItem
        {
            public double ta;
            public List<double[]> args;
        }

        List<FormulaItem> getFixedFormula()
        {
            List<FormulaItem> formula = new List<FormulaItem>();
            List<double[]> args = new List<double[]>();
            args.Add(new double[] { 46.5, 105.0, 256.0, 19.92 });
            args.Add(new double[] { 44.5, 210.0, 256.0, 0.82 });
            args.Add(new double[] { 42.5, 40.0, 256.0, 30.2});
            args.Add(new double[] { 40.5, 58.0, 256.0, 27.2 });
            args.Add(new double[] { -10000, 35.0, 256.0, 30.83 });
            formula.Add(new FormulaItem(){ ta=36.5, args=args });

            args = new List<double[]>();
            args.Add(new double[] { 41.5, 220.0, 256.0, 1.42 });
            args.Add(new double[] { 40.5, 60.0, 256.0, 27.28 });
            args.Add(new double[] { 39.5, 56.0, 256.0, 28.0 });
            args.Add(new double[] { 38.5, 38.0, 256.0, 30.56 });
            args.Add(new double[] { -10000, 70.0, 256.0, 25.88 });
            formula.Add(new FormulaItem() { ta = 31.5, args = args });

            args = new List<double[]>();
            args.Add(new double[] { 39.5, 100.0, 256.0, 23.45 });
            args.Add(new double[] { 38.5, 130.0, 256.0, 18.79 });
            args.Add(new double[] { 37.5, 250.0, 256.0, 0.7 });
            args.Add(new double[] { 36.5, 58.0, 256.0, 28.71 });
            args.Add(new double[] { 35.5, 38.0, 256.0, 31.52 });
            args.Add(new double[] { -10000, 71.0, 256.0, 26.83 });
            formula.Add(new FormulaItem() { ta = 27.0, args = args });

            args = new List<double[]>();
            args.Add(new double[] { 36.5, 130.0, 256.0, 19.12 });
            args.Add(new double[] { 34.5, 89.0, 256.0, 24.95 });
            args.Add(new double[] { 32.5, 60.0, 256.0, 28.85 });
            args.Add(new double[] { 31.5, 58.0, 256.0, 29.1 });
            args.Add(new double[] { -10000, 90.0, 256.0, 25.15 });
            formula.Add(new FormulaItem() { ta = 19.0, args = args });

            args = new List<double[]>();
            args.Add(new double[] { 36.5, 200.0, 256.0, 9.13 });
            args.Add(new double[] { 34.5, 100.0, 256.0, 23.39 });
            args.Add(new double[] { 32.5, 51.0, 256.0, 29.99 });
            args.Add(new double[] { 31.5, 58.0, 256.0, 29.1 });
            args.Add(new double[] { -10000, 80.0, 256.0, 26.45 });
            formula.Add(new FormulaItem() { ta = 10.0, args = args });

            return formula;
        }

        List<FormulaItem> mFormula = null;

        double adjustTemp(double TB, double TA)
        {
            if(mFormula == null)
            {
                mFormula = getFixedFormula();
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
                        var filePath = System.IO.Path.Combine(sdCardPath, "formula.txt");
                        if (Directory.Exists(sdCardPath))
                        {
                            StreamReader sr = new StreamReader(filePath);
                            string line;
                            mFormula = new List<FormulaItem>();
                            double ta = 0;
                            List<double[]> args = new List<double[]>();
                            while((line = sr.ReadLine()) != null)
                            {
                                line = line.Trim();
                                if ("".Equals(line))
                                    continue;
                                string[] subs = line.Split(",");
                                if(subs.Length == 4)
                                {
                                    double[] doubles = Array.ConvertAll(subs, Double.Parse);
                                    args.Add(doubles);
                                } 
                                else if (subs.Length == 1)
                                {
                                    if(args.Count > 0)
                                    {
                                        mFormula.Add(new FormulaItem() { ta = ta, args = args });
                                        args = new List<double[]>();
                                    }
                                    ta = Convert.ToDouble(subs[0]);
                                }
                            }
                            if (args.Count > 0)
                            {
                                mFormula.Add(new FormulaItem() { ta = ta, args = args });
                            }
                        }
                    }
                }
                catch
                {
                    
                }
            }
            return calculateTemp(TB, TA);
        }

        double calculateTemp(double TB, double TA)
        {
            double tmp0 = 0;
            List<double[]> args = null;
            foreach(FormulaItem item in mFormula)
            {
                if(item.ta <= TA)
                {
                    args = item.args;
                    break;
                }
            }
            if(args != null)
            {
                foreach(double[] arg in args)
                {
                    if(arg[0] <= TB)
                    {
                        tmp0 = arg[1] / arg[2] * TB + arg[3];
                        break;
                    }
                }
            }
            return tmp0;
        }

        double adjustTemp_fixed(double TB, double TA)
        {
            double tmp0 = 0;
            if (36.5 <= TA)
            {
                if (46.5 <= TB)
                    tmp0 = (105.0 / 256.0) * TB + 19.92;
                else if (44.5 <= TB)
                    tmp0 = (210.0 / 256.0) * TB + 0.82;
                else if (42.5 <= TB)
                    tmp0 = (40.0 / 256.0) * TB + 30.2;
                else if (40.5 <= TB)
                    tmp0 = (58.0 / 256.0) * TB + 27.2;
                else
                    tmp0 = (35.0 / 256.0) * TB + 30.83;
            } else if (31 <= TA)
            {
                if (41.5 <= TB)
                    tmp0 = (220.0 / 256.0) * TB + 1.42;
                else if (40.5 <= TB)
                    tmp0 = (60.0 / 256.0) * TB + 27.28;
                else if (39.5 <= TB)
                    tmp0 = (56.0 / 256.0) * TB + 28;
                else if (38.5 <= TB)
                    tmp0 = (38.0 / 256.0) * TB + 30.56;
                else
                    tmp0 = (70.0 / 256.0) * TB + 25.88;
            } else if (27 <= TA)
            {
                if (39.5 <= TB)
                    tmp0 = (100.0 / 256.0) * TB + 23.45;
                else if (38.5 <= TB)
                    tmp0 = (130.0 / 256.0) * TB + 18.79;
                else if (37.5 <= TB)
                    tmp0 = (250.0 / 256.0) * TB + 0.7;
                else if (36.5 <= TB)
                    tmp0 = (58.0 / 256.0) * TB + 28.71;
                else if (35.5 <= TB)
                    tmp0 = (38.0 / 256.0) * TB + 31.52;
                else
                    tmp0 = (71.0 / 256.0) * TB + 26.83;
            } else if (19 <= TA)
            {
                if (36.5 <= TB)
                    tmp0 = (130.0 / 256.0) * TB + 19.12;
                else if (34.5 <= TB)
                    tmp0 = (89.0 / 256.0) * TB + 24.95;
                else if (32.5 <= TB)
                    tmp0 = (60.0 / 256.0) * TB + 28.85;
                else if (31.5 <= TB)
                    tmp0 = (58.0 / 256.0) * TB + 29.1;
                else
                    tmp0 = (90.0 / 256.0) * TB + 25.15;
            } else if (10 <= TA)
            {
                if (36.5 <= TB)
                    tmp0 = (200.0 / 256.0) * TB + 9.13;
                else if (34.5 <= TB)
                    tmp0 = (100.0 / 256.0) * TB + 23.39;
                else if (32.5 <= TB)
                    tmp0 = (51.0 / 256.0) * TB + 29.99;
                else if (31.5 <= TB)
                    tmp0 = (58.0 / 256.0) * TB + 29.1;
                else
                    tmp0 = (80.0 / 256.0) * TB + 26.45;
            }

            return tmp0;
        }
        //double adjustTemp(double InValue, double TA)
        //{

        //    double tmp0 = 0;
        //    double tmp1 = 0;

        //    if ((TA >= 27.0) && (TA < 31.0))
        //    {
        //        do
        //        {

        //            if ((InValue >= 38.5) && (InValue < 39.5))
        //            {
        //                tmp0 = (200.0 / 256.0) * InValue + 6.45;
        //                break;
        //            }

        //            if ((InValue >= 37.5) && (InValue < 38.5))
        //            {
        //                tmp0 = (40.0 / 256.0) * InValue + 30.66;
        //                break;
        //            }

        //            if ((InValue < 37.5) && (InValue >= 36.5))
        //            {
        //                tmp0 = (58.0 / 256.0) * InValue + 28.2;
        //                break;
        //            }

        //            if ((InValue < 36.5) && (InValue >= 35.5))
        //            {
        //                tmp0 = (35.0 / 256.0) * InValue + 31.55;
        //                break;
        //            }

        //            if (InValue < 35.5)
        //            {
        //                tmp0 = (39.0 / 256.0) * InValue + 31.1;
        //                break;
        //            }

        //            if (InValue >= 39.5)
        //            {
        //                tmp0 = (40.0 / 256.0) * InValue + 31.39;
        //                break;
        //            }


        //        } while (false);

        //        return tmp0;
        //    }

        //    if (TA >= 31)
        //    {
        //        do
        //        {

        //            if (InValue >= 41.5)
        //            {
        //                tmp0 = (220.0 / 256.0) * InValue + 1.42;
        //                break;
        //            }

        //            if ((InValue >= 40.5) && (InValue < 41.5))
        //            {
        //                tmp0 = (60.0 / 256.0) * InValue + 27.28;
        //                break;
        //            }

        //            if ((InValue >= 39.5) && (InValue < 40.5))
        //            {
        //                tmp0 = (56.0 / 256.0) * InValue + 28;
        //                break;
        //            }

        //            if ((InValue >= 38.5) && (InValue < 39.5))
        //            {
        //                tmp0 = (38.0 / 256.0) * InValue + 30.56;
        //                break;
        //            }

        //            if (InValue < 38.5)
        //            {
        //                tmp0 = (70.0 / 256.0) * InValue + 25.88;
        //                break;
        //            }

        //        } while (false);

        //        return tmp0;
        //    }

        //    if ((TA < 27) && (TA >= 19))
        //    {
        //        do
        //        {

        //            if (InValue < 31.5)
        //            {
        //                tmp0 = (80.0 / 256.0) * InValue + 26.71;
        //                break;
        //            }

        //            if ((InValue >= 31.5) && (InValue < 32.5))
        //            {
        //                tmp0 = (58.0 / 256.0) * InValue + 29.1;
        //                break;
        //            }

        //            if ((InValue >= 32.5) && (InValue < 34.5))
        //            {
        //                tmp0 = (61.0 / 256.0) * InValue + 28.5;
        //                break;
        //            }

        //            if ((InValue >= 34.5) && (InValue < 36.5))
        //            {
        //                tmp0 = (90.0 / 256.0) * InValue + 24.37;
        //                break;
        //            }
        //            if (InValue >= 36.5)
        //            {
        //                tmp0 = (245.0 / 256.0) * InValue + 2.35;
        //                break;
        //            }

        //        } while (false);

        //        return tmp0;
        //    }

        //    if ((TA < 19) && (TA >= 10))
        //    {
        //        do
        //        {

        //            if (InValue < 31.5)
        //            {
        //                tmp0 = (80.0 / 256.0) * InValue + 6.58;
        //                break;
        //            }

        //            if ((InValue >= 31.5) && (InValue < 32.5))
        //            {
        //                tmp0 = (58.0 / 256.0) * InValue + 29.03;
        //                break;
        //            }

        //            if ((InValue >= 32.5) && (InValue < 34.5))
        //            {
        //                tmp0 = (51.0 / 256.0) * InValue + 28.45;
        //                break;
        //            }

        //            if ((InValue >= 34.5) && (InValue < 35.5))
        //            {
        //                tmp0 = (190.0 / 256.0) * InValue + 10.99;
        //                break;
        //            }

        //            if (InValue >= 35.5)
        //            {
        //                tmp0 = (245.0 / 256.0) * InValue + 2.23;
        //                break;
        //            }

        //        } while (false);

        //        return tmp0;
        //    }


        //    return 0;
        //}

        //double adjustTemp(double InValue, double TA)
        //{

        //    double tmp0 = 0;
        //    double tmp1 = 0;

        //    if ((TA >= 27.0) && (TA < 34.0))
        //    {
        //        do
        //        {

        //            if ((InValue >= 38.5) && (InValue < 39.5))
        //            {
        //                tmp0 = (200.0 / 256.0) * InValue + 6.05;
        //                break;
        //            }

        //            if ((InValue >= 37.5) && (InValue < 38.5))
        //            {
        //                tmp0 = (40.0 / 256.0) * InValue + 30.66;
        //                break;
        //            }

        //            if ((InValue < 37.5) && (InValue >= 36.5))
        //            {
        //                tmp0 = (58.0 / 256.0) * InValue + 28.2;
        //                break;
        //            }

        //            if ((InValue < 36.5) && (InValue >= 35.5))
        //            {
        //                tmp0 = (35.0 / 256.0) * InValue + 31.55;
        //                break;
        //            }

        //            if ((InValue < 35.5) && (InValue >= 34.5))
        //            {
        //                tmp0 = (39 / 256.0) * InValue + 31.55;
        //                break;
        //            }
        //            if ((InValue < 36.5) && (InValue >= 35.5))
        //            {
        //                tmp0 = (35.0 / 256.0) * InValue + 31.55;
        //                break;
        //            }

        //            if (InValue < 32.5)
        //            {
        //                tmp0 = (80.0 / 256.0) * InValue + 26.61;
        //                break;
        //            }

        //            if (InValue >= 39.5)
        //            {
        //                tmp0 = (40.0 / 256.0) * InValue + 31.39;
        //                break;
        //            }

        //        } while (false);

        //        return tmp0;
        //    }

        //    if (TA >= 34)
        //    {
        //        do
        //        {

        //            if (InValue >= 41.5)
        //            {
        //                tmp0 = (220.0 / 256.0) * InValue + 0.5;
        //                break;
        //            }

        //            if ((InValue >= 40.5) && (InValue < 41.5))
        //            {
        //                tmp0 = (60.0 / 256.0) * InValue + 26.88;
        //                break;
        //            }

        //            if ((InValue >= 39.5) && (InValue < 40.5))
        //            {
        //                tmp0 = (56.0 / 256.0) * InValue + 27.8;
        //                break;
        //            }

        //            if ((InValue >= 38.5) && (InValue < 39.5))
        //            {
        //                tmp0 = (38.0 / 256.0) * InValue + 30.56;
        //                break;
        //            }
        //            if ((InValue >= 37.5) && (InValue < 38.5))
        //            {
        //                tmp0 = (70.0 / 256.0) * InValue + 25.98;
        //                break;
        //            }

        //            if (InValue < 37.5)
        //            {
        //                tmp0 = (70.0 / 256.0) * InValue + 26.78;
        //                break;
        //            }

        //        } while (false);

        //        return tmp0;
        //    }

        //    if ((TA < 27) && (TA >= 19))
        //    {
        //        do
        //        {

        //            if (InValue < 31.5)
        //            {
        //                tmp0 = (80.0 / 256.0) * InValue + 26.71;
        //                break;
        //            }

        //            if ((InValue >= 31.5) && (InValue < 32.5))
        //            {
        //                tmp0 = (58.0 / 256.0) * InValue + 29.1;
        //                break;
        //            }

        //            if ((InValue >= 32.5) && (InValue < 34.5))
        //            {
        //                tmp0 = (61.0 / 256.0) * InValue + 28.5;
        //                break;
        //            }

        //            if ((InValue >= 34.5) && (InValue < 36.5))
        //            {
        //                tmp0 = (90.0 / 256.0) * InValue + 24.37;
        //                break;
        //            }
        //            if (InValue >= 36.5)
        //            {
        //                tmp0 = (245.0 / 256.0) * InValue + 2.35;
        //                break;
        //            }

        //        } while (false);

        //        return tmp0;
        //    }

        //    if ((TA < 19) && (TA >= 10))
        //    {
        //        do
        //        {

        //            if (InValue < 31.5)
        //            {
        //                tmp0 = (80.0 / 256.0) * InValue + 6.58;
        //                break;
        //            }

        //            if ((InValue >= 31.5) && (InValue < 32.5))
        //            {
        //                tmp0 = (58.0 / 256.0) * InValue + 29.03;
        //                break;
        //            }

        //            if ((InValue >= 32.5) && (InValue < 34.5))
        //            {
        //                tmp0 = (51.0 / 256.0) * InValue + 28.45;
        //                break;
        //            }

        //            if ((InValue >= 34.5) && (InValue < 35.5))
        //            {
        //                tmp0 = (190.0 / 256.0) * InValue + 10.99;
        //                break;
        //            }

        //            if (InValue >= 35.5)
        //            {
        //                tmp0 = (245.0 / 256.0) * InValue + 2.23;
        //                break;
        //            }

        //        } while (false);

        //        return tmp0;
        //    }


        //    return 0;
        //}

        //double adjustTemp(double InValue, double TA)
        //{
        //    double tmp0 = 0;
        //    double tahigh0 = 0;
        //    double talow0 = 0;

        //    if (TA <= 27.5 && InValue > 36.5)
        //    {
        //        tahigh0 = (double)(34.84 + 0.148 * (TA - 25.0));
        //        talow0 = (double)(32.66 + 0.186 * (TA - 25.0));

        //        do
        //        {

        //            if (InValue > tahigh0)
        //            {
        //                tmp0 = (double)(36.8 + (0.829320617815896 + 0.0023644335442161 * TA) * (InValue - tahigh0));
        //                break;
        //            }

        //            else if (InValue < talow0)
        //            {
        //                tmp0 = (double)(36.3 + (0.551658272522697 + 0.0215250684640259 * TA) * (InValue - talow0));
        //                break;
        //            }

        //            else if ((InValue <= tahigh0) && (InValue >= talow0))
        //            {
        //                tmp0 = (double)(36.3 + 0.5 / (tahigh0 - talow0) * (InValue - talow0));
        //                break;
        //            }

        //        } while (false);

        //        return tmp0 = tmp0 - 1.5;
        //    }

        //    if ((TA < 27.5) && (TA >= 19) && (InValue <= 36.5))
        //    {
        //        do
        //        {

        //            if (InValue < 31.5)
        //            {
        //                tmp0 = (double)((90.0 / 256.0) * InValue + 25.15);
        //                break;
        //            }

        //            if ((InValue >= 31.5) && (InValue < 32.5))
        //            {
        //                tmp0 = (double)((58.0 / 256.0) * InValue + 29.10);
        //                break;
        //            }

        //            if ((InValue >= 32.5) && (InValue < 34.5))
        //            {
        //                tmp0 = (double)((60.0 / 256.0) * InValue + 28.85);
        //                break;
        //            }

        //            if ((InValue >= 34.5) && (InValue < 36.5))
        //            {
        //                tmp0 = (double)((89.0 / 256.0) * InValue + 24.95);
        //                break;
        //            }
        //            if (InValue >= 36.5)
        //            {
        //                tmp0 = (double)((130.0 / 256.0) * InValue + 19.12);
        //                break;
        //            }

        //        } while (false);

        //        return tmp0 = tmp0 - 0.6;
        //    }

        //    if ((TA > 27.5) && (TA <= 31.5) && (InValue >= 39.5))
        //    {
        //        tahigh0 = (double)(34.84 + 0.1 * (TA - 25.0));
        //        talow0 = (double)(32.66 + 0.086 * (TA - 25.0));

        //        do
        //        {

        //            if (InValue > tahigh0)
        //            {
        //                tmp0 = (double)(36.8 + (0.829320617815896 + 0.0023644335442161 * TA) * (InValue - tahigh0));
        //                break;
        //            }

        //            else if (InValue < talow0)
        //            {
        //                tmp0 = (double)(36.3 + (0.551658272522697 + 0.0215250684640259 * TA) * (InValue - talow0));
        //                break;
        //            }

        //            else if ((InValue <= tahigh0) && (InValue >= talow0))
        //            {
        //                tmp0 = (double)(36.3 + 0.5 / (tahigh0 - talow0) * (InValue - talow0));
        //                break;
        //            }

        //        } while (false);

        //        return tmp0 = tmp0 - 4.5;
        //    }

        //    if ((TA > 27.5) && (TA <= 31.5) && (InValue < 39.5))
        //    {
        //        do
        //        {

        //            if (InValue < 35.5)
        //            {
        //                tmp0 = (double)((69.0 / 256.0) * InValue + 26.83);
        //                break;
        //            }

        //            else if ((InValue < 36.5) && (InValue >= 35.5))
        //            {
        //                tmp0 = (double)((35.0 / 256.0) * InValue + 31.55);
        //                break;
        //            }

        //            else if ((InValue < 37.5) && (InValue >= 36.5))
        //            {
        //                tmp0 = (double)((58.0 / 256.0) * InValue + 28.2);
        //                break;
        //            }

        //            else if ((InValue < 38.5) && (InValue >= 37.5))
        //            {
        //                tmp0 = (double)((40.0 / 256.0) * InValue + 30.81);
        //                break;
        //            }

        //            else if ((InValue < 39.5) && (InValue >= 38.5))
        //            {
        //                tmp0 = (double)((130.0 / 256.0) * InValue + 16.78);
        //                break;
        //            }

        //        } while (false);

        //        return tmp0 = tmp0 - 0.0;
        //    }

        //    if ((TA > 31.5) && (TA <= 36.5) && (InValue < 41.5))
        //    {
        //        do
        //        {

        //            if (InValue < 38.5)
        //            {
        //                tmp0 = (double)((70.0 / 256.0) * InValue + 25.88);
        //                break;
        //            }

        //            if ((InValue < 38.5) && (InValue >= 39.5))
        //            {
        //                tmp0 = (double)((38.0 / 256.0) * InValue + 30.56);
        //                break;
        //            }

        //            if ((InValue < 39.5) && (InValue >= 40.5))
        //            {
        //                tmp0 = (double)((56.0 / 256.0) * InValue + 28.0);
        //                break;
        //            }

        //            if ((InValue < 40.5) && (InValue >= 41.5))
        //            {
        //                tmp0 = (double)((60.0 / 256.0) * InValue + 27.28);
        //                break;
        //            }

        //            if (InValue >= 41.5)
        //            {
        //                tmp0 = (double)((220.0 / 256.0) * InValue + 1.42);
        //                break;
        //            }

        //        } while (false);

        //        return tmp0 = tmp0 - 0.0;
        //    }

        //    if ((TA > 31.5) && (TA <= 36.5) && (InValue >= 41.5))
        //    {
        //        tahigh0 = (double)(34.84 + 0.1 * (TA - 25.0));
        //        talow0 = (double)(32.66 + 0.086 * (TA - 25.0));

        //        do
        //        {

        //            if (InValue > tahigh0)
        //            {
        //                tmp0 = (double)(36.8 + (0.829320617815896 + 0.0023644335442161 * TA) * (InValue - tahigh0));
        //                break;
        //            }

        //            else if (InValue < talow0)
        //            {
        //                tmp0 = (double)(36.3 + (0.551658272522697 + 0.0215250684640259 * TA) * (InValue - talow0));
        //                break;
        //            }

        //            else if ((InValue <= tahigh0) && (InValue >= talow0))
        //            {
        //                tmp0 = (double)(36.3 + 0.5 / (tahigh0 - talow0) * (InValue - talow0));
        //                break;
        //            }

        //        } while (false);

        //        return tmp0 = tmp0 - 4.5;
        //    }

        //    if (TA >= 36.5 && InValue < 46.5)
        //    {
        //        do
        //        {

        //            if (InValue >= 40.5)
        //            {
        //                tmp0 = (double)((35.0 / 256.0) * InValue + 30.83);
        //                break;
        //            }

        //            if ((InValue >= 40.5) && (InValue < 42.5))
        //            {
        //                tmp0 = (double)((58.0 / 256.0) * InValue + 27.2);
        //                break;
        //            }

        //            if ((InValue >= 42.5) && (InValue < 44.5))
        //            {
        //                tmp0 = (double)((40.0 / 256.0) * InValue + 30.0);
        //                break;
        //            }

        //            if ((InValue >= 44.5) && (InValue < 46.5))
        //            {
        //                tmp0 = (double)((130.0 / 256.0) * InValue + 14.41);
        //                break;
        //            }

        //            if (InValue < 46.5)
        //            {
        //                tmp0 = (double)((100.0 / 256.0) * InValue + 19.92);
        //                break;
        //            }

        //        } while (false);

        //        return tmp0 = tmp0 - 0.0;
        //    }

        //    if (TA > 36.5 && InValue >= 46.5)
        //    {
        //        tahigh0 = (double)(34.84 + 0.1 * (TA - 25.0));
        //        talow0 = (double)(32.66 + 0.086 * (TA - 25.0));

        //        do
        //        {

        //            if (InValue > tahigh0)
        //            {
        //                tmp0 = (double)(36.8 + (0.829320617815896 + 0.0023644335442161 * TA) * (InValue - tahigh0));
        //                break;
        //            }

        //            else if (InValue < talow0)
        //            {
        //                tmp0 = (double)(36.3 + (0.551658272522697 + 0.0215250684640259 * TA) * (InValue - talow0));
        //                break;
        //            }

        //            else if ((InValue <= tahigh0) && (InValue >= talow0))
        //            {
        //                tmp0 = (double)(36.3 + 0.5 / (tahigh0 - talow0) * (InValue - talow0));
        //                break;
        //            }

        //        } while (false);

        //        return tmp0 = tmp0 - 9.7;
        //    }
        //    return 0;
        //}
    }
}