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
        const short EEPROM_SIZE = 192;

        UsbSerialPort port;

        UsbManager usbManager;
        TextView titleTextView;
        TextView dumpTextView;
        ScrollView scrollView;
        TextView tvLatest;

        Button btnStart;
        Button btnStop;
        Button btnClear;
        Button btnLoadFromFile;
        Button btnLoadFromEEPROM;
        Button btnSaveToEEPROM;

        enum OPERATION
        {
            IDLE,
            INIT,
            READ,
            LOADEEPROM,
            SAVEEEPROM,
        };

        OPERATION mOperation;

        SerialInputOutputManager serialIoManager;
        private System.Timers.Timer timer = null;
        int messageCount = 0;

        struct FormulaItem
        {
            public double ta;
            public List<double[]> args;
        }
        List<FormulaItem> mFormula = getFixedFormula();

        struct Matrix
        {
            public int startRow;
            public int startCol;
            public int rowCount;
            public int colCount;
        };

        struct Formula2Item
        {
            public double ta;
            public double arg1;
            public double arg2;
            public double arg3;
            public double arg4;
            public double arg5;
            public double arg6;
            public double arg7;
        };

        struct Formula2
        {
            public double taForMatrix;
            public Matrix lowMatrix;
            public Matrix highMatrix;
            public List<Formula2Item> entries;
        }

        Formula2 mFormula2 = getFixedFormula2();

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
            btnLoadFromFile = FindViewById<Button>(Resource.Id.loadFromFile);
            btnLoadFromEEPROM = FindViewById<Button>(Resource.Id.loadFromEEPROM);
            btnSaveToEEPROM = FindViewById<Button>(Resource.Id.saveToEEPROM);

            //loadFormulaFromFile();
            loadFormula2FromFile();

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

            btnLoadFromFile.Click += delegate
            {
                //loadFormulaFromFile();
                loadFormula2FromFile();
            };

            btnLoadFromEEPROM.Click += delegate
            {
                onBtnLoadFromEEPROM();
            };

            btnSaveToEEPROM.Click += delegate
            {
                switchOperation(OPERATION.SAVEEEPROM);
                if (serialIoManager.IsOpen)
                {
                    byte[] writeCmd = serializeFomulaToSaveEEPROM();
                    if(writeCmd != null)
                        port.Write(writeCmd, WRITE_WAIT_MILLIS);
                }
            };
        }

        void onBtnLoadFromEEPROM()
        {
            switchOperation(OPERATION.LOADEEPROM);
            if (serialIoManager.IsOpen)
            {
                port.Write(loadEEPROM, WRITE_WAIT_MILLIS);
            }
        }

        void loadFormulaFromFile()
        {
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
                        List<FormulaItem> formula = new List<FormulaItem>();
                        double ta = 0;
                        List<double[]> args = new List<double[]>();
                        while ((line = sr.ReadLine()) != null)
                        {
                            line = line.Trim();
                            if ("".Equals(line))
                                continue;
                            string[] subs = line.Split(",");
                            if (subs.Length == 4)
                            {
                                double[] doubles = Array.ConvertAll(subs, Double.Parse);
                                args.Add(doubles);
                            }
                            else if (subs.Length == 1)
                            {
                                if (args.Count > 0)
                                {
                                    formula.Add(new FormulaItem() { ta = ta, args = args });
                                    args = new List<double[]>();
                                }
                                ta = Convert.ToDouble(subs[0]);
                            }
                        }
                        if (args.Count > 0)
                        {
                            formula.Add(new FormulaItem() { ta = ta, args = args });
                        }

                        mFormula = formula;
                        Toast.MakeText(Application.Context, "Loaded formula from formula.txt on SD Card", ToastLength.Long).Show();
                    }
                }
            }
            catch
            {

            }
        }

        void loadFormula2FromFile()
        {
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
                    var filePath = System.IO.Path.Combine(sdCardPath, "formula2.txt");
                    if (Directory.Exists(sdCardPath))
                    {
                        StreamReader sr = new StreamReader(filePath);
                        string line;
                        Formula2 f = new Formula2();
                        line = sr.ReadLine();
                        if (line == null)
                        {
                            Toast.MakeText(Application.Context, "Failed to load formula from formula2.txt on SD Card", ToastLength.Long).Show();
                            return;
                        }
                        string[] subs = line.Trim().Split(",");
                        if(subs.Length != 9)
                        {
                            Toast.MakeText(Application.Context, "Incorrect format in line 1 of formula2.txt on SD Card", ToastLength.Long).Show();
                        }
                        double[] doubles = Array.ConvertAll(subs, Double.Parse);
                        f.taForMatrix = doubles[0];
                        Matrix m = new Matrix();
                        m.startRow = (int)doubles[1];
                        m.startCol = (int)doubles[2];
                        m.rowCount = (int)doubles[3];
                        m.colCount = (int)doubles[4];
                        f.lowMatrix = m;
                        m = new Matrix();
                        m.startRow = (int)doubles[5];
                        m.startCol = (int)doubles[6];
                        m.rowCount = (int)doubles[7];
                        m.colCount = (int)doubles[8];
                        f.highMatrix = m;
                        f.entries = new List<Formula2Item>();

                        while ((line = sr.ReadLine()) != null)
                        {
                            line = line.Trim();
                            if ("".Equals(line))
                                continue;
                            subs = line.Split(",");
                            if (subs.Length == 8)
                            {
                                doubles = Array.ConvertAll(subs, Double.Parse);
                                Formula2Item e = new Formula2Item();
                                e.ta = doubles[0];
                                e.arg1 = doubles[1];
                                e.arg2 = doubles[2];
                                e.arg3 = doubles[3];
                                e.arg4 = doubles[4];
                                e.arg5 = doubles[5];
                                e.arg6 = doubles[6];
                                e.arg7 = doubles[7];
                                f.entries.Add(e);
                            }
                        }

                        mFormula2 = f;
                        Toast.MakeText(Application.Context, "Loaded formula2 from formula2.txt on SD Card", ToastLength.Long).Show();
                    }
                }
            }
            catch
            {
                Toast.MakeText(Application.Context, "Unable to load formula2 from formula2.txt on SD Card", ToastLength.Long).Show();
            }
        }

        void switchOperation(OPERATION op)
        {
            if(op == OPERATION.IDLE)
            {
                btnStart.Enabled = true;
                //btnStop.Enabled = true;
                btnLoadFromEEPROM.Enabled = true;
                btnSaveToEEPROM.Enabled = true;
            }
            else
            {
                btnStart.Enabled = false;
                //btnStop.Enabled = true;
                btnLoadFromEEPROM.Enabled = false;
                btnSaveToEEPROM.Enabled = false;
            }
            mOperation = op;
        }

        byte[] init0data = new byte[] { 0xAC, 0xD0, 0x2F, 0x04, 0x00 };
        byte[] initdata = new byte[] { 0xAD, 0x00, 0x02, 0xD0, 0xB0, 0x10 };
        byte[] getdata = new byte[] { 0xad, 0x02, 0x0d, 0xd0, 0x4e };
        byte[] loadEEPROM = new byte[] { 0xE2, 0x00, 0x00, (byte)(EEPROM_SIZE>>8), (byte)(EEPROM_SIZE & 0xff)};

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
                switchOperation(OPERATION.INIT);
                WriteData(init0data);
                await Task.Delay(200);
                WriteData(initdata);
                await Task.Delay(200);
                switchOperation(OPERATION.IDLE);
                //onBtnLoadFromEEPROM();
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
            if(mOperation == OPERATION.INIT)
            {
                return;
            }
            else if(mOperation == OPERATION.LOADEEPROM)
            {
                switchOperation(OPERATION.IDLE);
                if(data.Length == EEPROM_SIZE+1 && data[0] == 0xE2)
                {
                    List<FormulaItem> formula = getFormulaFromEEPROMData(data);
                    if (formula != null)
                    {
                        mFormula = formula;
                        Toast.MakeText(Application.Context, "Loaded Formula From EEPROM", ToastLength.Long).Show();
                    }
                }
                else
                {
                    Toast.MakeText(Application.Context, "Load From EEPROM Failed", ToastLength.Long).Show();
                }
                return;
            }
            else if(mOperation == OPERATION.SAVEEEPROM)
            {
                switchOperation(OPERATION.IDLE);
                if(data.Length > 0 && data[0] == 0xE1)
                {
                    Toast.MakeText(Application.Context, "Saved formula to EEPROM", ToastLength.Long).Show();
                }
                else
                {
                    Toast.MakeText(Application.Context, "Save to EEPROM Failed", ToastLength.Long).Show();
                }
                return;
            }
            //handle read data
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
            double ambientTemp = data2Temp(ambient);

            int startRow = 5, startCol = 5, rowCount = 6, colCount = 6;

            //use new matrix 
            if(ambientTemp < mFormula2.taForMatrix)
            {
                startRow = mFormula2.lowMatrix.startRow;
                startCol = mFormula2.lowMatrix.startCol;
                rowCount = mFormula2.lowMatrix.rowCount;
                colCount = mFormula2.lowMatrix.colCount;
            } else
            {
                startRow = mFormula2.highMatrix.startRow;
                startCol = mFormula2.highMatrix.startCol;
                rowCount = mFormula2.highMatrix.rowCount;
                colCount = mFormula2.highMatrix.colCount;
            }
            //end use new matrix

            int startLine = 13 + 2 * 16 * startRow; //skip startRow lines
            int maxCenter = 0;
            for (int j = 0; j < rowCount; j++, startLine += 2 * 16)
            {
                for (int i = 0, index = startLine + 2 * startCol; i < colCount; i++, index += 2)
                {
                    int data = frame[index] * 256 + frame[index + 1];
                    if (data > maxCenter)
                        maxCenter = data;
                }
            }
            
            double maxCenterTemp = data2Temp(maxCenter);
            //double adjustedTemp = adjustTemp(maxCenterTemp, ambientTemp);
            //double adjustedTemp = adjustTemp_alg0710(maxCenterTemp, ambientTemp);
            double adjustedTemp = adjustTemp_formula2(maxCenterTemp, ambientTemp);

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

        short doubleToShort(double v)
        {
            v *= 100;
            short sh;
            if (v > Int16.MaxValue)
                sh = Int16.MaxValue;
            else if (v < Int16.MinValue)
                sh = Int16.MinValue;
            else
                sh = Convert.ToInt16(v);
            return sh;
        }

        void fillData(byte[] data, int p, double v)
        {
            short sh = doubleToShort(v);
            data[p] = (byte)(sh >> 8);
            data[p+1] = (byte)(sh & 0xff);
        }

        double fetchData(byte[] data, int p)
        {
            short v = (short)((data[p] << 8) | (data[p + 1]));
            return v / 100.0;
        }

        byte[] serializeFomulaToSaveEEPROM()
        {
            byte[] data = new byte[EEPROM_SIZE + 5];
            try
            {                
                data[0] = 0xE1;
                data[1] = 0x00;
                data[2] = 0x00;
                data[3] = (byte)(EEPROM_SIZE >> 8);
                data[4] = (byte)(EEPROM_SIZE & 0xff);
                int p = 5;

                if(mFormula.Count > Byte.MaxValue)
                {
                    Toast.MakeText(Application.Context, "Formula contains too many items!", ToastLength.Long).Show();
                    return null;
                }
                data[p++] = (byte)mFormula.Count;
                foreach (FormulaItem item in mFormula)
                {
                    fillData(data, p, item.ta);
                    p += 2;
                    if (item.args.Count > Byte.MaxValue)
                    {
                        Toast.MakeText(Application.Context, "Formula contains too many items!", ToastLength.Long).Show();
                        return null;
                    }
                    data[p++] = (byte)item.args.Count;
                    foreach (double[] arg in item.args)
                    {
                        for (int i = 0; i < 4; ++i)
                        {
                            if (i == 2)
                                continue; // skip 256.0
                            fillData(data, p, arg[i]);
                            p += 2;
                        }
                    }
                }
                if (p > EEPROM_SIZE + 3)
                {
                    Toast.MakeText(Application.Context, "Formula oversize!", ToastLength.Long).Show();
                    return null;
                }
                data[EEPROM_SIZE+3] = 0x55;
                data[EEPROM_SIZE+4] = 0xAA;
            } catch(IndexOutOfRangeException)
            {
                Toast.MakeText(Application.Context, "Formula oversize!", ToastLength.Long).Show();
                return null;
            }            
            return data;
        }

        List<FormulaItem> getFormulaFromEEPROMData(byte[] data)
        {
            if(data.Length != EEPROM_SIZE+1)
            {
                Toast.MakeText(Application.Context, "Incorrect EEPROM formula size!", ToastLength.Long).Show();
                return null;
            }
            if(data[0] != 0xE2 || data[EEPROM_SIZE-1] != 0x55 || data[EEPROM_SIZE] != 0xAA)
            {
                Toast.MakeText(Application.Context, "Incorrect EEPROM formula signature!", ToastLength.Long).Show();
                return null;
            }
            int p = 1;
            List<FormulaItem> formula = new List<FormulaItem>();
            byte n = data[p++];
            for(int i = 0; i < n; ++i)
            {
                double ta = fetchData(data, p);
                p += 2;
                byte k = data[p++];
                List<double[]> args = new List<double[]>();
                for (int j = 0; j < k; ++j)
                {
                    double[] arg = new double[4];
                    for (int s = 0; s < 4; ++s)
                    {
                        if(s == 2)
                        {
                            arg[s] = 256.0;
                            continue; //skip 256.0
                        }
                        arg[s] = fetchData(data, p);
                        p += 2;
                    }
                    args.Add(arg);
                }
                formula.Add(new FormulaItem() { ta = ta, args = args });
            }
            
            return formula;
        }

        static Formula2 getFixedFormula2()
        {
            Formula2 f = new Formula2();
            f.taForMatrix = 30.0;
            Matrix m = new Matrix();
            m.startRow = 5;
            m.startCol = 5;
            m.rowCount = 6;
            m.colCount = 6;
            f.lowMatrix = m;
            m = new Matrix();
            m.startRow = 5;
            m.startCol = 5;
            m.rowCount = 6;
            m.colCount = 6;
            f.highMatrix = m;
            List<Formula2Item> l = new List<Formula2Item>();
            Formula2Item e = new Formula2Item();
            e.ta = 25;
            e.arg1 = 34.84;
            e.arg2 = 0.148;
            e.arg3 = -25.0;
            e.arg4 = 32.66;
            e.arg5 = 0.186;
            e.arg6 = -25.0;
            e.arg7 = 0.0;
            l.Add(e);
            e = new Formula2Item();
            e.ta = 29;
            e.arg1 = 34.84;
            e.arg2 = 0.100;
            e.arg3 = 10.0;
            e.arg4 = 32.66;
            e.arg5 = 0.086;
            e.arg6 = 10.0;
            e.arg7 = 0.0;
            l.Add(e);
            e = new Formula2Item();
            e.ta = 32; 
            e.arg1 = 34.84; 
            e.arg2 = 0.100; 
            e.arg3 = 35.0; 
            e.arg4 = 32.66; 
            e.arg5 = 0.086; 
            e.arg6 = 35.0; 
            e.arg7 = -0.2;
            l.Add(e);
            e = new Formula2Item();
            e.ta = 35; 
            e.arg1 = 34.84; 
            e.arg2 = 0.100; 
            e.arg3 = 65.0; 
            e.arg4 = 32.66; 
            e.arg5 = 0.086; 
            e.arg6 = 65.0; 
            e.arg7 = 0.0;
            l.Add(e);
            e = new Formula2Item();
            e.ta = 38; 
            e.arg1 = 34.84; 
            e.arg2 = 0.100; 
            e.arg3 = 85.0; 
            e.arg4 = 32.66; 
            e.arg5 = 0.086; 
            e.arg6 = 85.0; 
            e.arg7 = 0.1;
            l.Add(e);
            e = new Formula2Item();
            e.ta = 10000;
            e.arg1 = 34.84; 
            e.arg2 = 0.100; 
            e.arg3 = 100.0; 
            e.arg4 = 32.66; 
            e.arg5 = 0.086; 
            e.arg6 = 100.0; 
            e.arg7 = 0.1;
            l.Add(e);
            f.entries = l;
            return f;
        }

        static List<FormulaItem> getFixedFormula()
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

        double adjustTemp_formula2(double InValue, double TA)
        {
            double tmp0 = 0;
            double tahigh0 = 0;
            double talow0 = 0;

            foreach(Formula2Item e in mFormula2.entries)
            {
                if(TA <= e.ta)
                {
                    tahigh0 = (double)(e.arg1 + e.arg2 * (TA + e.arg3));
                    talow0 = (double)(e.arg4 + e.arg5 * (TA + e.arg6));

                    if (InValue > tahigh0)
                    {
                        tmp0 = (double)(36.8 + (0.829320617815896 + 0.0023644335442161 * TA) * (InValue - tahigh0));
                    }
                    else if (InValue < talow0)
                    {
                        tmp0 = (double)(36.3 + (0.551658272522697 + 0.0215250684640259 * TA) * (InValue - talow0));
                    }
                    else if ((InValue <= tahigh0) && (InValue >= talow0))
                    {
                        tmp0 = (double)(36.3 + 0.5 / (tahigh0 - talow0) * (InValue - talow0));
                    }
                    tmp0 = tmp0 + e.arg7;
                    break;
                }
            }
            return tmp0;
        }

        double adjustTemp_alg0710(double InValue, double TA)
        {
            double tmp0 = 0;
            double tahigh0 = 0;
            double talow0 = 0;

            if (TA <= 25)
            {
                tahigh0 = (double)(34.84 + 0.148 * (TA - 30.0));
                talow0 = (double)(32.66 + 0.186 * (TA - 30.0));

                if (InValue > tahigh0)
                {
                    tmp0 = (double)(36.8 + (0.829320617815896 + 0.0023644335442161 * TA) * (InValue - tahigh0));
                }
                else if (InValue < talow0)
                {
                    tmp0 = (double)(36.3 + (0.551658272522697 + 0.0215250684640259 * TA) * (InValue - talow0));
                }
                else if ((InValue <= tahigh0) && (InValue >= talow0))
                {
                    tmp0 = (double)(36.3 + 0.5 / (tahigh0 - talow0) * (InValue - talow0));
                }
                tmp0 = tmp0 + 0.2;
            }

            else if ((TA > 25) && (TA < 30))
            {
                tahigh0 = (double)(34.84 + 0.100 * (TA - 50.0));
                talow0 = (double)(32.66 + 0.086 * (TA - 50.0));

                if (InValue > tahigh0)
                {
                    tmp0 = (double)(36.8 + (0.829320617815896 + 0.0023644335442161 * TA) * (InValue - tahigh0));
                }
                else if (InValue < talow0)
                {
                    tmp0 = (double)(36.3 + (0.551658272522697 + 0.0215250684640259 * TA) * (InValue - talow0));
                }
                else if ((InValue <= tahigh0) && (InValue >= talow0))
                {
                    tmp0 = (double)(36.3 + 0.5 / (tahigh0 - talow0) * (InValue - talow0));
                }
                tmp0 = tmp0 - 0.2;
            }

            else if ((TA >= 30) && (TA < 33))
            {
                tahigh0 = (double)(34.84 + 0.100 * (TA - 30.0));
                talow0 = (double)(32.66 + 0.086 * (TA - 30.0));

                if (InValue > tahigh0)
                {
                    tmp0 = (double)(36.8 + (0.829320617815896 + 0.0023644335442161 * TA) * (InValue - tahigh0));
                }
                else if (InValue < talow0)
                {
                    tmp0 = (double)(36.3 + (0.551658272522697 + 0.0215250684640259 * TA) * (InValue - talow0));
                }
                else if ((InValue <= tahigh0) && (InValue >= talow0))
                {
                    tmp0 = (double)(36.3 + 0.5 / (tahigh0 - talow0) * (InValue - talow0));
                }
                tmp0 = tmp0 - 0.0;
            }

            else if ((TA >= 33) && (TA < 35))
            {
                tahigh0 = (double)(34.84 + 0.100 * (TA - 20.0));
                talow0 = (double)(32.66 + 0.086 * (TA - 20.0));

                if (InValue > tahigh0)
                {
                    tmp0 = (double)(36.8 + (0.82932061781586 + 0.0023644335442161 * TA) * (InValue - tahigh0));
                }
                else if (InValue < talow0)
                {
                    tmp0 = (double)(36.3 + (0.551658272522697 + 0.0215250684640259 * TA) * (InValue - talow0));
                }
                else if ((InValue <= tahigh0) && (InValue >= talow0))
                {
                    tmp0 = (double)(36.3 + 0.5 / (tahigh0 - talow0) * (InValue - talow0));
                }
                tmp0 = tmp0 - 0.0;
            }

            else if (TA >= 35)
            {
                tahigh0 = (double)(34.84 + 0.100 * (TA - 15.0));
                talow0 = (double)(32.66 + 0.086 * (TA - 15.0));

                if (InValue > tahigh0)
                {
                    tmp0 = (double)(36.8 + (0.82932061781586 + 0.0023644335442161 * TA) * (InValue - tahigh0));
                }
                else if (InValue < talow0)
                {
                    tmp0 = (double)(36.3 + (0.551658272522697 + 0.0215250684640259 * TA) * (InValue - talow0));
                }
                else if ((InValue <= tahigh0) && (InValue >= talow0))
                {
                    tmp0 = (double)(36.3 + 0.5 / (tahigh0 - talow0) * (InValue - talow0));
                }
                tmp0 = tmp0 - 0.0;
            }
            return tmp0;
        }

        double adjustTemp(double TB, double TA)
        {
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
    }
}