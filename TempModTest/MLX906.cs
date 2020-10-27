using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Android.App;
using Android.Content;
using Android.OS;
using Android.Runtime;
using Android.Util;
using Android.Views;
using Android.Widget;
using Hoho.Android.UsbSerial.Driver;

namespace TempModTest_MLX906
{
    class MLX906
    {
        static readonly string TAG = typeof(MLX906).Name;

        static readonly byte[] CMD_ResetHardware = new byte[] { 0 };
        static readonly byte[] CMD_GetHardwareID = new byte[] { 1 };
        static readonly byte[] CMD_GetSoftwareID = new byte[] { 3 };
        static readonly byte[] CMD_ReadEEPROM = new byte[] { 10 };
        static readonly byte[] CMD_I2C_Master_SW = new byte[] { 31 };
        static readonly byte[] CMD_SetVdd = new byte[] { 150 };
        static readonly byte[] CMD_StopDAQ = new byte[] { 152 };
        static readonly byte[] CMD_ReadDAQ = new byte[] { 153 };
        static readonly byte[] CMD_MeasureVddIdd = new byte[] { 156 };
        static readonly byte[] CMD_StartDAQ_90640 = new byte[] { 171 };
        static readonly byte[] CMD_I2C_Master_90640 = new byte[] { 174 };
        static readonly byte[] CMD_StartDAQ_90641 = new byte[] { 179 };

        static readonly (byte[], byte[]) __command_response_pairs_init_sw_i2c = (new byte[] { 0x1E, 2, 0, 0, 0, 6, 0, 0, 0, 8, 0, 0, 0, 5, 0, 0, 0 }, new byte[] { 0x1E });
        static readonly (byte[], byte[]) __command_response_pairs_begin_conversion = (new byte[] { 0xAE, 0x33, 0x80, 0x00, 0x00, 0x20, 0x00, 0x00 }, new byte[] { 0xAE, 0x00 });

        const int DAQ_CONT_16x12 = 5;
        const double MIN_TEMP_DEGC = -273.15;
        const double LSB_DEGC = 50.0;

        const int READ_WAIT_MILLIS = 200;
        const int WRITE_WAIT_MILLIS = 200;

        static byte[] Combine(byte[] first, byte[] second)
        {
            byte[] ret = new byte[first.Length + second.Length];
            Buffer.BlockCopy(first, 0, ret, 0, first.Length);
            Buffer.BlockCopy(second, 0, ret, first.Length, second.Length);
            return ret;
        }

        UsbSerialPort port;
        byte i2c_addr;
        double frame_rate;
        TCalcParams calc_params;
        int m_lFilterTgcDepth;
        int[][] m_arrLastTgc;
        double m_fEmissivity;
        int sensor_type;

        bool support_buffer;
        List<short[]> frames_buffer;
        int m_lDaqFrameIdx;
        int frame_length_bytes;

        IMlxEEPROM eeprom;

        static AutoResetEvent dataEvent = new AutoResetEvent(false);
        static byte[] dataBuf = new byte[0];
        static int dataBufIdx = 0;
        private static readonly object dataLock = new object();

        public static void pushData(byte[] buf)
        {
            //Log.Info(TAG, "PushData: " + BitConverter.ToString(buf.Take(10).ToArray()));

            lock (dataLock)
            {
                if(dataBuf.Length - dataBufIdx == 0)
                {
                    dataBuf = buf;
                    dataBufIdx = 0;
                }
                else
                {
                    dataBuf = Combine(dataBuf.Skip(dataBufIdx).Take(dataBuf.Length - dataBufIdx).ToArray(), buf);
                    dataBufIdx = 0;
                }
                
            }

            dataEvent.Set();
        }

        static byte[] popData(int n)
        {
            while(true)
            {
                lock (dataLock)
                {
                    if (dataBuf.Length - dataBufIdx >= n)
                    {
                        byte[] pop = new byte[n];
                        Array.Copy(dataBuf, dataBufIdx, pop, 0, n);
                        dataBufIdx += n;
                        //int newLength = dataBuf.Length - n;
                        //dataBuf = dataBuf.Skip(n).Take(newLength).ToArray();
                        return pop;
                    }
                }
                bool received = dataEvent.WaitOne(3000);
                if (!received)
                    throw new Exception("receive data timeout");
            }
        }

        static void clearData()
        {
            lock(dataLock)
            {
                dataBuf = new byte[0];
                dataBufIdx = 0;
            }
        }

        public MLX906(UsbSerialPort port, byte i2c_addr = 0x33,  double frame_rate = 8.0, double emissivity = 1.0)
        {
            this.port = port;
            this.i2c_addr = i2c_addr;
            this.frame_rate = frame_rate;
            this.calc_params = new TCalcParams();
            m_lFilterTgcDepth = 8;
            m_arrLastTgc = new int[TCalcParams.NUM_TGC][];
            for (int i = 0; i < TCalcParams.NUM_TGC; ++i)
            {
                m_arrLastTgc[i] = Enumerable.Repeat(0, TCalcParams.NUM_PAGES).ToArray();
            }
            this.m_fEmissivity = emissivity;

            this.support_buffer = false;
            this.frames_buffer = null;
            this.m_lDaqFrameIdx = 0;

            this.Connect();
            this.SetFrameRate(this.frame_rate);
            if (this.sensor_type == 0)
                this.eeprom = new Mlx90640EEPROM(this);
            else
                this.eeprom = new Mlx90641EEPROM(this);
        }

        public void Init()
        {
            this.eeprom.ReadEEPROMFromDevice();
            this.CalculateParameters();
        }

        public (double[], double) DoCompensation(short[] raw_frame)
        {
            int np;
            if (this.sensor_type == 0)
                np = 32 * 24;
            else
                np = 16 * 12;

            short[] info_data = raw_frame.Skip(np).Take(raw_frame.Length - np).ToArray();

            byte[] data;
            byte status;
            if (this.sensor_type == 1)
            {
                //# ToDo: get VDD_pix out of info_data
                (data, status) = this.I2CRead(0x05AA, 2);
                if (0 != status)
                    throw new Exception("Error during read of Control register 1");
                ushort val = (ushort)StructConverter.Unpack(">H", data)[0];
                info_data[42] = (short)(val > 32767 ? val - 65536 : val);

                //# ToDo: get Ta_PTAT out of info_data
                (data, status) = this.I2CRead(0x05A0, 2);
                if (0 != status)
                    throw new Exception("Error during read of Control register 1");
                val = (ushort)StructConverter.Unpack(">H", data)[0];
                info_data[32] = (short)(val > 32767 ? val - 65536 : val);

                //# ToDo: get Ta_VBE out of info_data
                (data, status) = this.I2CRead(0x0580, 2);
                if (0 != status)
                    throw new Exception("Error during read of Control register 1");
                val = (ushort)StructConverter.Unpack(">H", data)[0];
                info_data[0] = (short)(val > 32767 ? val - 65536 : val);

                //# ToDo: get GAIN_RAM out of info_data
                (data, status) = this.I2CRead(0x058A, 2);
                if (0 != status)
                    throw new Exception("Error during read of Control register 1");
                val = (ushort)StructConverter.Unpack(">H", data)[0];
                info_data[10] = (short)(val > 32767 ? val - 65536 : val);

                //# ToDo: get tgcValue out of info_data
                (data, status) = this.I2CRead(0x0588, 2);
                if (0 != status)
                    throw new Exception("Error during read of Control register 1");
                val = (ushort)StructConverter.Unpack(">H", data)[0];
                info_data[8] = (short)(val > 32767 ? val - 65536 : val);
            }

            //# Calculation of actual Vdd [V] by MLX9064x
            if (Math.Abs(this.calc_params.Kv_Vdd) < 1e-6)
                throw new Exception("Kv_Vdd is too small");

            double vdd_meas = this.calc_params.Vdd_V0 + (info_data[42] - this.calc_params.Vdd_25) / this.calc_params.Kv_Vdd;

            //# Calculation of ambient temperature
            double ptat_sc = info_data[32];

            double d = (ptat_sc * this.calc_params.alpha_ptat + info_data[0]);
            if (Math.Abs(d) < 1e-6)
                throw new Exception("Can't calculate VPTAT_virt");

            double VPTAT_virt = ptat_sc / d * (1 << 18);

            if (Math.Abs(this.calc_params.Kt_PTAT) < 1e-6)
                throw new Exception("Kt_PTAT is too small");

            double d2 = (1 + (vdd_meas - this.calc_params.Vdd_V0) * this.calc_params.Kv_PTAT);
            if (Math.Abs(d2) < 1e-6)
                throw new Exception("Kv_PTAT is not correct");

            double Tamb = (VPTAT_virt / d2 - this.calc_params.VPTAT_25) / this.calc_params.Kt_PTAT + 25;
            info_data[2] = (short)Math.Round( (Tamb - MLX906.MIN_TEMP_DEGC) * MLX906.LSB_DEGC);

            int lControl1 = this.eeprom.GetParameterCode(ParameterCodesEEPROM.CodeControl1);

            int tidx = 0;
            if (this.calc_params.version >= 2)
            {
                double minTdiff = 9999.0;
                int minTidx = 0;
                for (tidx = 0; tidx < TCalcParams.MAX_CAL_RANGES; ++tidx)
                {
                    if (this.calc_params.Ta_min[tidx] <= Tamb && Tamb <= this.calc_params.Ta_max[tidx])
                        break;
                    //# If Ta doesn't fall in any region? => Use the closest reg
                    double tdiff = Math.Min(Math.Abs(Tamb - this.calc_params.Ta_min[tidx]), Math.Abs(Tamb - this.calc_params.Ta_max[tidx]));
                    if (tdiff < minTdiff)
                    {
                        minTdiff = tdiff;
                        minTidx = tidx;
                    }
                }

                if (tidx >= TCalcParams.MAX_CAL_RANGES)
                    tidx = minTidx;
            }

            double dDeltaTa = Tamb - this.calc_params.Ta0[tidx];
            double dDeltaV = vdd_meas - this.calc_params.Vdd_V0;

            if (info_data[10] == 0)
                throw new Exception("Can't do gain drift compensation");
            double dGainComp = this.calc_params.GainMeas_25_3v2 / info_data[10];

            //# Compensate cyclops
            int[] pCyclopIdx0 = new int[] { 8, 9 };
            int[] pCyclopIdx1 = new int[] { 0x28, 0x29 };
            //# int page = m_lDaqFrameIdx & 1;
            double[] arrdCyclops = Enumerable.Repeat(0.0, TCalcParams.NUM_PAGES).ToArray();  //# accumulates all cyclops
            double dKsTa = 1.0;
            double[] arrdAlphaCyclops = Enumerable.Repeat(0.0, TCalcParams.NUM_PAGES).ToArray();
            for (int page = 0; page < TCalcParams.NUM_PAGES; ++page)
            {
                arrdCyclops[page] = 0;
                arrdAlphaCyclops[page] = 0;
            }

            if (this.calc_params.version >= 2)
            {
                dKsTa = 1 + this.calc_params.KsTa * (Tamb - this.calc_params.Ta_0_Alpha);
                if (Math.Abs(dKsTa) < 1e-12)
                    throw new Exception("Calculated KsTa is zero");
                for (int page = 0; page < TCalcParams.NUM_PAGES; ++page)
                {
                    int[] pCyclopIdx = (page != 0) ? pCyclopIdx1 : pCyclopIdx0;
                    for (int i = 0; i < TCalcParams.NUM_TGC; ++i)
                    {
                        if (Math.Abs(this.calc_params.TGC[i]) > 1e-12)
                        {
                            int tgcValue = info_data[pCyclopIdx[i]];
                            if ((this.m_lDaqFrameIdx & (~1)) == 0 && this.m_lFilterTgcDepth > 1)
                            {
                                this.m_arrLastTgc[i][page] = tgcValue;
                            }
                            else if (this.m_lFilterTgcDepth > 1)
                            {
                                tgcValue = (this.m_arrLastTgc[i][page] * (this.m_lFilterTgcDepth - 1) + tgcValue); // self.m_lFilterTgcDepth
                                this.m_arrLastTgc[i][page] = tgcValue;
                                info_data[pCyclopIdx[i]] = (short)tgcValue; //round(tgcValue);  //# Allow logging of filtered TGC
                            }


                            //# 1. Gain drift compensation
                            double Pix_GainComp = tgcValue * dGainComp;

                            //# 2. Pixel offset compensation
                            double Pix_os = this.calc_params.Pix_os_ref_TGC[tidx][page][i] * (1 + this.calc_params.Kta_TGC[tidx][page][i] * dDeltaTa) * (1 + this.calc_params.Kv_TGC[tidx][page][i] * dDeltaV);

                            //# 3. calculating offset free IR data
                            arrdCyclops[page] += (Pix_GainComp - Pix_os) * this.calc_params.TGC[i];
                            arrdAlphaCyclops[page] += this.calc_params.alpha_TGC[page][i] * this.calc_params.TGC[i];
                        }
                    }
                }
            }

            dKsTa *= this.m_fEmissivity;
            if (Math.Abs(dKsTa) < 1e-12)
                throw new Exception("Calculated KsTa is zero");

            double dTaPow4 = Math.Pow(Tamb - MLX906.MIN_TEMP_DEGC, 4);

            //# resulting frame without service data
            double[] result_frame;
            if (this.sensor_type == 0)
                result_frame = Enumerable.Repeat(0.0, 32 * 24).ToArray();
            else
                result_frame = Enumerable.Repeat(0.0, 16 * 12).ToArray();

            for (int i = 0; i < np; ++i)
            {
                int page;
                if ((lControl1 & (1 << 12)) != 0)
                    page = (i & 1) ^ ((i / 32) & 1);  //# Chess pattern mode
                else
                    page = (i / 32) % 2;  //# Interlaced  mode
                int idxGlobal = i;

                //# 1. Gain drift compensation
                double Pix_GainComp = raw_frame[i] * dGainComp;
                double Pix_os;
                //# 2. Pixel offset compensation
                if (this.sensor_type == 0)
                    Pix_os = this.calc_params.Pix_os_ref[tidx][idxGlobal] * (1 + this.calc_params.Kta[tidx][idxGlobal] * dDeltaTa) * (1 + this.calc_params.Kv[tidx][idxGlobal] * dDeltaV);
                else if (page != 0)
                    Pix_os = this.calc_params.Pix_os_ref_SP1[tidx][idxGlobal] * (1 + this.calc_params.Kta[tidx][idxGlobal] * dDeltaTa) * (1 + this.calc_params.Kv[tidx][idxGlobal] * dDeltaV);
                else
                    Pix_os = this.calc_params.Pix_os_ref_SP0[tidx][idxGlobal] * (1 + this.calc_params.Kta[tidx][idxGlobal] * dDeltaTa) * (1 + this.calc_params.Kv[tidx][idxGlobal] * dDeltaV);

                //# 3. calculating offset free IR data
                double Pix_comp = Pix_GainComp - Pix_os;

                //# Calculate object temperature
                double alpha = this.calc_params.alpha[idxGlobal] - arrdAlphaCyclops[page];
                double To;
                if (Math.Abs(alpha) < 1e-12)
                    To = MLX906.MIN_TEMP_DEGC;
                else
                {
                    //# pass1
                    d = (Pix_comp - arrdCyclops[page]) / dKsTa / alpha + dTaPow4;
                    if (d < 0.0)
                        To = MLX906.MIN_TEMP_DEGC;
                    else
                    {
                        //# pass2
                        double To1 = Math.Pow(d, 0.25) + MLX906.MIN_TEMP_DEGC;
                        if (this.calc_params.version >= 2)
                        {
                            double dKsTo = 1 + this.calc_params.KsTo * (To1 - this.calc_params.To_0_Alpha);
                            d = (Pix_comp - arrdCyclops[page]) / dKsTa / dKsTo / alpha + dTaPow4;
                            if (d < 0.0)
                                To = MLX906.MIN_TEMP_DEGC;
                            else
                                To = Math.Pow(d, 0.25) + MLX906.MIN_TEMP_DEGC;
                        }
                        else
                            To = To1;
                    }
                }

                result_frame[i] = To;
            }
            return (result_frame, Tamb);
        }

        public short[] ReadFrame()
        {
            if (this.frames_buffer == null || this.frames_buffer.Count == 0)
            {
                this.frames_buffer = this.ReadFrames();
                if (this.frames_buffer == null || this.frames_buffer.Count == 0)
                    return null;
            }
            short[] frame = this.frames_buffer[0];
            this.frames_buffer.RemoveAt(0);
            return frame;
        }

        public void StopRead()
        {
            this.SendCommand(CMD_StopDAQ);
        }

        public void ClearError()
        {
            this.SendCommand(CMD_StopDAQ);
            this.SetVdd(3.3);
            int wait_us = (int)Math.Ceiling(1000000.0 / this.frame_rate);

            byte[] cmd;
            if (this.sensor_type == 0)
                cmd = Combine(CMD_StartDAQ_90640, StructConverter.Pack(new object[] { this.i2c_addr, wait_us }));
            else
                cmd = Combine(CMD_StartDAQ_90641, StructConverter.Pack(new object[] { this.i2c_addr, wait_us }));
            this.SendCommand(cmd);
        }

        List<short[]> ReadFrames()
        {
            byte[] data = this.SendCommand(Combine(CMD_ReadDAQ, new byte[] { 0 }));
            if(data[1] != 0)
            {
                throw new Exception("EVB90640: EVB Frame buffer full");
            }
            List<short[]> frames = null;
            int received_data_len = data.Length - 2;
            if (received_data_len >= this.frame_length_bytes)
            {
                if (received_data_len % this.frame_length_bytes != 0)
                    throw new Exception("Invalid data length from EVB");
                frames = new List<short[]>();
                for (int i = 0; i < received_data_len / this.frame_length_bytes; ++i)
                {
                    this.m_lDaqFrameIdx += 1;
                    int frame_data_start = 2 + i * this.frame_length_bytes;
                    int frame_data_end = 2 + (i + 1) * this.frame_length_bytes;
                    short[] frame =  new short[this.frame_length_bytes / 2];
                    for (int idx = frame_data_start, j = 0; idx < frame_data_end; idx += 2, ++j)
                    {
                        frame[j] = (short)((data[idx] << 8) | data[idx + 1]);
                    }
                    frames.Add(frame);
                }
            }
            return frames;
        }

        void CalculateParameters()
        {
            if(this.sensor_type == 0)
            {
                this.CalculateParameters_Type0();
            } else
            {
                this.CalculateParameters_Type1();
            }
        }

        void CalculateParameters_Type0()
        {
            this.calc_params.version = 5;

            this.calc_params.Id0 = this.eeprom.GetParameterCode(ParameterCodesEEPROM.CodeID1);
            this.calc_params.Id1 = this.eeprom.GetParameterCode(ParameterCodesEEPROM.CodeID2);
            this.calc_params.Id2 = this.eeprom.GetParameterCode(ParameterCodesEEPROM.CodeID3);

            int l = this.eeprom.GetParameterCode(ParameterCodesEEPROM.CodeVdd_25);
            this.calc_params.Vdd_25 = (l - 256) * (1 << 5) - (1 << 13);
            l = this.eeprom.GetParameterCode(ParameterCodesEEPROM.CodeK_Vdd);
            this.calc_params.Kv_Vdd = ((sbyte)l) * (1 << 5);//c_int8(l).value * (1 << 5)
            l = this.eeprom.GetParameterCode(ParameterCodesEEPROM.CodeRes_control);
            this.calc_params.Res_scale = (1 << l);
            l = this.eeprom.GetParameterCode(ParameterCodesEEPROM.CodePTAT_25);
            this.calc_params.VPTAT_25 = (short)l;//c_int16(l).value
            l = this.eeprom.GetParameterCode(ParameterCodesEEPROM.CodeKv_PTAT);
            this.calc_params.Kv_PTAT = (l > 31 ? l - 64 : l) / (1 << 12); //((l - 64) if (l > 31) else l) / (1 << 12)
            l = this.eeprom.GetParameterCode(ParameterCodesEEPROM.CodeKt_PTAT);
            this.calc_params.Kt_PTAT = (l > 511 ? l - 1024 : l) / (1 << 3);
            l = this.eeprom.GetParameterCode(ParameterCodesEEPROM.CodeAlpha_PTAT);
            this.calc_params.alpha_ptat = l / 4 + 8;
            l = this.eeprom.GetParameterCode(ParameterCodesEEPROM.CodeGAIN);
            this.calc_params.GainMeas_25_3v2 = (short)l;//c_int16(l).value

            l = this.eeprom.GetParameterCode(ParameterCodesEEPROM.CodePix_os_average);
            int Pix_os_average = (short)l;//c_int16(l).value
            l = this.eeprom.GetParameterCode(ParameterCodesEEPROM.CodeScale_Occ_rem);
            int Scale_occ_rem = 1 << l;
            l = this.eeprom.GetParameterCode(ParameterCodesEEPROM.CodeScale_Occ_col);
            int Scale_occ_col = 1 << l;
            l = this.eeprom.GetParameterCode(ParameterCodesEEPROM.CodeScale_Occ_row);
            int Scale_occ_row = 1 << l;
            int[] OccRow = Enumerable.Repeat(0, 24).ToArray();
            int[] OccCol = Enumerable.Repeat(0, 32).ToArray();

            for (int r = 0; r < 24; ++r)
            {
                l = this.eeprom.GetParameterCode(ParameterCodesEEPROM.CodeOCC_row, r);
                OccRow[r] = (l > 7 ? l - 16 : l) * Scale_occ_row;
            }

            for (int c = 0; c < 32; ++c)
            {
                l = this.eeprom.GetParameterCode(ParameterCodesEEPROM.CodeOCC_column, c);
                OccCol[c] = (l > 7 ? l - 16 : l) * Scale_occ_col;
            }

            for (int r = 0; r < 24; ++r)
            {
                for (int c = 0; c < 32; ++c)
                {
                    int idx = r * 32 + c;
                    l = this.eeprom.GetParameterCode(ParameterCodesEEPROM.CodePixel_Offset, idx);
                    this.calc_params.Pix_os_ref[0][idx] = Pix_os_average + OccRow[r] + OccCol[c] + (l > 31 ? l - 64 : l) * Scale_occ_rem;
                    for (int t = 1; t < TCalcParams.MAX_CAL_RANGES; ++t)
                        this.calc_params.Pix_os_ref[t][idx] = this.calc_params.Pix_os_ref[0][idx];
                }
            }

            l = this.eeprom.GetParameterCode(ParameterCodesEEPROM.CodeAlpha_scale);
            double Alpha_scale = (1L << l) * (1 << 30);
            l = this.eeprom.GetParameterCode(ParameterCodesEEPROM.CodePix_sens_average);
            int Pix_sens_average = (short)l; // c_int16(l).value
            l = this.eeprom.GetParameterCode(ParameterCodesEEPROM.CodeScale_Acc_rem);
            int Scale_Acc_rem = 1 << l;
            l = this.eeprom.GetParameterCode(ParameterCodesEEPROM.CodeScale_Acc_col);
            int Scale_Acc_col = 1 << l;
            l = this.eeprom.GetParameterCode(ParameterCodesEEPROM.CodeScale_Acc_row);
            int Scale_Acc_row = 1 << l;
            int[] AccRow = Enumerable.Repeat(0, 24).ToArray();
            int[] AccCol = Enumerable.Repeat(0, 32).ToArray();

            for (int r = 0; r < 24; ++r)
            {
                l = this.eeprom.GetParameterCode(ParameterCodesEEPROM.CodeACC_row, r);
                AccRow[r] = (l > 7 ? l - 16 : l) * Scale_Acc_row;
            }

            for (int c = 0; c < 32; ++c)
            {
                l = this.eeprom.GetParameterCode(ParameterCodesEEPROM.CodeACC_column, c);
                AccCol[c] = (l > 7 ? l - 16 : l) * Scale_Acc_col;
            }

            for (int r = 0; r < 24; ++r)
            {
                for (int c = 0; c < 32; ++c)
                {
                    int idx = r * 32 + c;
                    l = this.eeprom.GetParameterCode(ParameterCodesEEPROM.CodePixel_Alpha, idx);
                    this.calc_params.alpha[idx] = (Pix_sens_average + AccRow[r] + AccCol[c] +
                                                   (l > 31 ? l - 64 : l) * Scale_Acc_rem) / Alpha_scale;
                }
            }

            int[][] Kta = new int[][] { new int[] { 0, 0 }, new int[] { 0, 0 } };
            l = this.eeprom.GetParameterCode(ParameterCodesEEPROM.CodeKta_scale1);
            int Kta_scale1 = 1 << (l + 8);
            l = this.eeprom.GetParameterCode(ParameterCodesEEPROM.CodeKta_scale2);
            int Kta_scale2 = 1 << l;
            l = this.eeprom.GetParameterCode(ParameterCodesEEPROM.CodeKta_Avg_RO_CO);
            Kta[0][0] = (sbyte)l; // c_int8(l).value
            l = this.eeprom.GetParameterCode(ParameterCodesEEPROM.CodeKta_Avg_RO_CE);
            Kta[0][1] = (sbyte)l; //c_int8(l).value
            l = this.eeprom.GetParameterCode(ParameterCodesEEPROM.CodeKta_Avg_RE_CO);
            Kta[1][0] = (sbyte)l; //c_int8(l).value
            l = this.eeprom.GetParameterCode(ParameterCodesEEPROM.CodeKta_Avg_RE_CE);
            Kta[1][1] = (sbyte)l; //c_int8(l).value
            for (int r = 0; r < 24; ++r)
            {
                for (int c = 0; c < 32; ++c)
                {
                    int idx = r * 32 + c;
                    l = this.eeprom.GetParameterCode(ParameterCodesEEPROM.CodePixel_Kta, idx);
                    this.calc_params.Kta[0][idx] = ((l > 3 ? l - 8 : l) * Kta_scale2 + Kta[r % 2][c % 2]) / (double)Kta_scale1;
                    for (int t = 1; t < TCalcParams.MAX_CAL_RANGES; ++t)
                        this.calc_params.Kta[t][idx] = this.calc_params.Kta[0][idx];
                }
            }

            double[][] Kv = new double[][] { new double[] { 0, 0 }, new double[] { 0, 0 } };
            l = this.eeprom.GetParameterCode(ParameterCodesEEPROM.CodeKv_scale);
            int Kv_scale = 1 << l;
            l = this.eeprom.GetParameterCode(ParameterCodesEEPROM.CodeKv_Avg_RO_CO);
            Kv[0][0] = (l > 7 ? l - 16 : l) / (double)Kv_scale;
            l = this.eeprom.GetParameterCode(ParameterCodesEEPROM.CodeKv_Avg_RO_CE);
            Kv[0][1] = (l > 7 ? l - 16 : l) / (double)Kv_scale;
            l = this.eeprom.GetParameterCode(ParameterCodesEEPROM.CodeKv_Avg_RE_CO);
            Kv[1][0] = (l > 7 ? l - 16 : l) / (double)Kv_scale;
            l = this.eeprom.GetParameterCode(ParameterCodesEEPROM.CodeKv_Avg_RE_CE);
            Kv[1][1] = (l > 7 ? l - 16 : l) / (double)Kv_scale;
            for (int r = 0; r < 24; ++r)
            {
                for (int c = 0; c < 32; ++c)
                {
                    int idx = r * 32 + c;
                    this.calc_params.Kv[0][idx] = Kv[r % 2][c % 2];
                    for (int t = 1; t < TCalcParams.MAX_CAL_RANGES; ++t)
                        this.calc_params.Kv[t][idx] = this.calc_params.Kv[0][idx];
                }
            }

            //# as of v.2
            //# self.calc_params.Vdd_V0 = 3.3;             # actual value doesn't affect the results
            this.calc_params.Ta_min[1] = -200.0;
            this.calc_params.Ta_max[1] = 0.0;
            this.calc_params.Ta0[1] = 25.0;
            this.calc_params.Ta_min[0] = -200.0;  //# Fixed only R2;
            this.calc_params.Ta_max[0] = 1000.0;
            this.calc_params.Ta0[0] = 25.0;
            l = this.eeprom.GetParameterCode(ParameterCodesEEPROM.CodeTemp_Step);
            int Temp_Step = l * 5; //  # TODO : (0-3) or (1-4)?
            l = this.eeprom.GetParameterCode(ParameterCodesEEPROM.CodeCT1);
            int ct1 = l * Temp_Step;
            l = this.eeprom.GetParameterCode(ParameterCodesEEPROM.CodeCT2);
            int ct2 = l * Temp_Step;
            this.calc_params.Ta_min[2] = 60.0;
            this.calc_params.Ta_max[2] = (double)ct1;
            this.calc_params.Ta0[2] = 25.0;
            this.calc_params.Ta_min[3] = (double)ct1;
            this.calc_params.Ta_max[3] = (double)ct2;
            this.calc_params.Ta0[3] = 25.0;

            //# TGC[0] is not used
            l = this.eeprom.GetParameterCode(ParameterCodesEEPROM.CodeAlpha_scale);
            Alpha_scale = (1L << l) * (1 << 27);
            l = this.eeprom.GetParameterCode(ParameterCodesEEPROM.CodeTGC);
            this.calc_params.TGC[1] = (sbyte)l / (double)(1 << 5); //c_int8(l).value / (1 << 5)
            l = this.eeprom.GetParameterCode(ParameterCodesEEPROM.CodeAlpha_CP_P0);
            this.calc_params.alpha_TGC[0][1] = l / (double)Alpha_scale;
            l = this.eeprom.GetParameterCode(ParameterCodesEEPROM.CodeAlpha_CP_P1_P0);
            this.calc_params.alpha_TGC[1][1] = this.calc_params.alpha_TGC[0][1] * (1.0 + (l > 31 ? l - 64 : l) / (double)(1 << 7));
            l = this.eeprom.GetParameterCode(ParameterCodesEEPROM.CodeOffset_CP_P0);
            this.calc_params.Pix_os_ref_TGC[0][0][1] = (double)(l > 511 ? l - 1024 : l); //c_double((l - 1024) if (l > 511) else l).value
            l = this.eeprom.GetParameterCode(ParameterCodesEEPROM.CodeOffset_CP_P1_P0);
            this.calc_params.Pix_os_ref_TGC[0][1][1] = (double)(l > 31 ? l - 64 : l) + this.calc_params.Pix_os_ref_TGC[0][0][1];
            l = this.eeprom.GetParameterCode(ParameterCodesEEPROM.CodeKta_CP);
            this.calc_params.Kta_TGC[0][0][1] = (sbyte)l / (double)Kta_scale1;
            this.calc_params.Kta_TGC[0][1][1] = this.calc_params.Kta_TGC[0][0][1];
            l = this.eeprom.GetParameterCode(ParameterCodesEEPROM.CodeKv_CP);
            this.calc_params.Kv_TGC[0][0][1] = (sbyte)l / (double)Kv_scale;
            this.calc_params.Kv_TGC[0][1][1] = this.calc_params.Kv_TGC[0][0][1];

            for (int page = 0; page < TCalcParams.NUM_PAGES; ++page)
            {
                for (int t = 1; t < TCalcParams.MAX_CAL_RANGES; ++t)
                {
                    this.calc_params.Pix_os_ref_TGC[t][page][1] = this.calc_params.Pix_os_ref_TGC[0][page][1];
                    this.calc_params.Kta_TGC[t][page][1] = this.calc_params.Kta_TGC[0][page][1];
                    this.calc_params.Kv_TGC[t][page][1] = this.calc_params.Kv_TGC[0][page][1];
                }
            }

            l = this.eeprom.GetParameterCode(ParameterCodesEEPROM.CodeKsTa);
            this.calc_params.KsTa = (sbyte)l / (double)(1 << 13);
            //# Ta_0_Alpha = 25;
            l = this.eeprom.GetParameterCode(ParameterCodesEEPROM.CodeScale_KsTo);
            int ScaleKsTo = 1 << (l + 8);
            //# Only R2 (extended -200-1000)degC
            l = this.eeprom.GetParameterCode(ParameterCodesEEPROM.CodeKsTo_R2);
            this.calc_params.KsTo =(sbyte)l / (double)ScaleKsTo;
            //# To_0_Alpha;            // default 0.0
        }

        void CalculateParameters_Type1()
        {
            this.calc_params.version = 5;

            this.calc_params.Id0 = this.eeprom.GetParameterCode(ParameterCodesEEPROM.CodeID1);
            this.calc_params.Id1 = this.eeprom.GetParameterCode(ParameterCodesEEPROM.CodeID2);
            this.calc_params.Id2 = this.eeprom.GetParameterCode(ParameterCodesEEPROM.CodeID3);

            int l = this.eeprom.GetParameterCode(ParameterCodesEEPROM.CodeVdd_25);
            this.calc_params.Vdd_25 = (l > 1023 ? l - 2048 : l) * 32;
            l = this.eeprom.GetParameterCode(ParameterCodesEEPROM.CodeK_Vdd);
            this.calc_params.Kv_Vdd = (l > 1023 ? l - 2048 : l) * 32;
            l = this.eeprom.GetParameterCode(ParameterCodesEEPROM.CodeCalib_res_cont);
            this.calc_params.Res_scale = l;
            l = 32 * this.eeprom.GetParameterCode((ParameterCodesEEPROM.CodePTAT_25), 0) + this.eeprom.GetParameterCode((ParameterCodesEEPROM.CodePTAT_25), 1);
            this.calc_params.VPTAT_25 = (short)l;//c_int16(l).value
            l = this.eeprom.GetParameterCode(ParameterCodesEEPROM.CodeKv_PTAT);
            this.calc_params.Kv_PTAT = (l > 1023 ? l - 2048 : l) / 4096.0;
            l = this.eeprom.GetParameterCode(ParameterCodesEEPROM.CodeKt_PTAT);
            this.calc_params.Kt_PTAT = (l > 1023 ? l - 2048 : l) / 8.0;
            l = this.eeprom.GetParameterCode(ParameterCodesEEPROM.CodeAlpha_PTAT);
            this.calc_params.alpha_ptat = l / 128.0;
            l = 32 * this.eeprom.GetParameterCode(ParameterCodesEEPROM.CodeGAIN, 0) + this.eeprom.GetParameterCode(ParameterCodesEEPROM.CodeGAIN, 1);
            this.calc_params.GainMeas_25_3v2 = (short)l; //c_int16(l).value

            l = 32 * this.eeprom.GetParameterCode((ParameterCodesEEPROM.CodePix_os_average), 0) + this.eeprom.GetParameterCode((ParameterCodesEEPROM.CodePix_os_average), 1);
            int offset_average = (l > 32767 ? l - 65536 : l);
            int offset_scale = this.eeprom.GetParameterCode((ParameterCodesEEPROM.CodeScale_occ_os));
            for (int r = 0; r < 12; ++r)
            {
                for (int c = 0; c < 16; ++c)
                {
                    int idx = r * 16 + c;
                    l = this.eeprom.GetParameterCode((ParameterCodesEEPROM.CodePixel_Offset), idx);
                    this.calc_params.Pix_os_ref_SP0[0][idx] = offset_average + (l > 1023 ? l - 2048 : l) * Math.Pow(2, offset_scale);
                    l = this.eeprom.GetParameterCode((ParameterCodesEEPROM.CodePixel_os), idx);
                    this.calc_params.Pix_os_ref_SP1[0][idx] = offset_average + (l > 1023 ? l - 2048 : l) * Math.Pow(2, offset_scale);
                    for (int t = 1; t < TCalcParams.MAX_CAL_RANGES; ++t)
                    {
                        this.calc_params.Pix_os_ref_SP0[t][idx] = this.calc_params.Pix_os_ref_SP0[0][idx];
                        this.calc_params.Pix_os_ref_SP1[t][idx] = this.calc_params.Pix_os_ref_SP1[0][idx];
                    }
                }
            }

            for (int r = 0; r < 12; ++r)
            {
                for (int c = 0; c < 16; ++c)
                {
                    int idx = r * 16 + c;
                    int pixel_row = ((16 * (r - 1) + c) - 1) / 32;
                    double alpha_reference = this.eeprom.GetParameterCode(ParameterCodesEEPROM.CodeRow_max, pixel_row) / Math.Pow(2, (this.eeprom.GetParameterCode(ParameterCodesEEPROM.CodeScale_row, pixel_row) + 20));
                    l = this.eeprom.GetParameterCode(ParameterCodesEEPROM.CodePixel_Sensitivity, idx);
                    this.calc_params.alpha[idx] = l / (2048.0 - 1) * alpha_reference;
                }
            }

            l = this.eeprom.GetParameterCode(ParameterCodesEEPROM.CodeKta_avg);
            int Kta = (l > 1023 ? l - 2048 : l);
            l = this.eeprom.GetParameterCode(ParameterCodesEEPROM.CodeKta_scale_1);
            double Kta_scale1 = Math.Pow(2, l);
            l = this.eeprom.GetParameterCode(ParameterCodesEEPROM.CodeKta_scale_2);
            double Kta_scale2 = Math.Pow(2, l);

            for (int r = 0; r < 12; ++r)
            {
                for (int c = 0; c < 16; ++c)
                {
                    int idx = r * 16 + c;
                    l = this.eeprom.GetParameterCode(ParameterCodesEEPROM.CodePixel_Kta, idx);
                    this.calc_params.Kta[0][idx] = ((l > 31 ? l - 64 : l) * Kta_scale2 + Kta) / Kta_scale1;
                    for (int t = 1; t < TCalcParams.MAX_CAL_RANGES; ++t)
                        this.calc_params.Kta[t][idx] = this.calc_params.Kta[0][idx];
                }
            }

            l = this.eeprom.GetParameterCode(ParameterCodesEEPROM.CodeKv_avg);
            int Kv = (l > 1023 ? l - 2048 : l);
            l = this.eeprom.GetParameterCode(ParameterCodesEEPROM.CodeKv_scale_1);
            double Kv_scale1 = Math.Pow(2, l);
            l = this.eeprom.GetParameterCode(ParameterCodesEEPROM.CodeKv_scale_2);
            double Kv_scale2 = Math.Pow(2, l);

            for (int r = 0; r < 12; ++r)
            {
                for (int c = 0; c < 16; ++c)
                {
                    int idx = r * 16 + c;
                    l = this.eeprom.GetParameterCode(ParameterCodesEEPROM.CodePixel_Kv, idx);
                    this.calc_params.Kv[0][idx] = ((l > 31 ? l - 64 : l) * Kv_scale2 + Kv) / Kv_scale1;
                    for (int t = 1; t < TCalcParams.MAX_CAL_RANGES; ++t)
                        this.calc_params.Kv[t][idx] = this.calc_params.Kv[0][idx];
                }
            }
            //# as of v.2
            //# self.calc_params.Vdd_V0 = 3.3;             # actual value doesn't affect the results
            this.calc_params.Ta_min[0] = -40.0;  //# Fixed only R2;
            this.calc_params.Ta_max[0] = 20.0;
            this.calc_params.Ta0[0] = 25.0;
            this.calc_params.Ta_min[1] = -20.0;
            this.calc_params.Ta_max[1] = 0.0;
            this.calc_params.Ta0[1] = 25.0;
            this.calc_params.Ta_min[2] = 0.0;
            this.calc_params.Ta_max[2] = 80.0;
            this.calc_params.Ta0[2] = 25.0;
            this.calc_params.Ta_min[3] = 80.0;
            this.calc_params.Ta_max[3] = 120.0;
            this.calc_params.Ta0[3] = 25.0;

            l = this.eeprom.GetParameterCode(ParameterCodesEEPROM.CodeTGC);
            this.calc_params.TGC[1] = 0; //#l / 2**6

            l = this.eeprom.GetParameterCode(ParameterCodesEEPROM.CodeAlpha_CP_scale);
            double Alpha_scale = l / Math.Pow(2, 38);
            l = this.eeprom.GetParameterCode(ParameterCodesEEPROM.CodeAlpha_CP);
            this.calc_params.alpha_TGC[0][1] = l / Alpha_scale;

            this.calc_params.Kta_TGC[0][0][1] = this.eeprom.GetParameterCode(ParameterCodesEEPROM.CodeKta_CP) / Math.Pow(2, (this.eeprom.GetParameterCode(ParameterCodesEEPROM.CodeKta_CP_scale)));
            this.calc_params.Kta_TGC[0][1][1] = this.calc_params.Kta_TGC[0][0][1];

            this.calc_params.Kv_TGC[0][0][1] = this.eeprom.GetParameterCode(ParameterCodesEEPROM.CodeKv_CP) / Math.Pow(2, (this.eeprom.GetParameterCode(ParameterCodesEEPROM.CodeKv_CP_scale)));
            this.calc_params.Kv_TGC[0][1][1] = this.calc_params.Kv_TGC[0][0][1];

            for (int page = 0; page < TCalcParams.NUM_PAGES; ++page)
            {
                for (int t = 1; t < TCalcParams.MAX_CAL_RANGES; ++t)
                {
                    this.calc_params.Kta_TGC[t][page][1] = this.calc_params.Kta_TGC[0][page][1];
                    this.calc_params.Kv_TGC[t][page][1] = this.calc_params.Kv_TGC[0][page][1];
                }
            }

            l = this.eeprom.GetParameterCode(ParameterCodesEEPROM.CodeKsTa);
            this.calc_params.KsTa = (l > 1023 ? l - 2048 : l) / Math.Pow(2, 15);
            //# Ta_0_Alpha = 25;
            l = this.eeprom.GetParameterCode(ParameterCodesEEPROM.CodeKsTo_scale);
            double ScaleKsTo = Math.Pow(2, l);
            //# Only R2 (extended -200-1000)degC
            l = this.eeprom.GetParameterCode(ParameterCodesEEPROM.CodeKsTo_R2);
            this.calc_params.KsTo = (l > 1023 ? l - 2048 : l) / ScaleKsTo;
            //# To_0_Alpha;            // default 0.0
        }

        public void ReadEEPromFromDevice()
        {
            (byte[] first_read, byte status) = I2CRead(0x2400, (ushort)this.eeprom.GetEEPROMSize());
            if (status != 0)
                throw new Exception("Error during initial read of eeprom");
            ushort[] eeprom = new ushort[this.eeprom.GetEEPROMSize() / 2];
            for(int i = 0, j = 0; i < first_read.Length; i += 2, ++j)
            {
                eeprom[j] = (ushort)( (first_read[i] << 8) | first_read[i + 1] );
            }
            byte[] consecutive_read;
            for(int m = 0;  m < 2; ++m)
            {
                Thread.Sleep(200);
                (consecutive_read, status) = I2CRead(0x2400, (ushort)this.eeprom.GetEEPROMSize());
                if (status != 0)
                    throw new Exception("Error during consecutive read of eeprom");
                for (int i = 0, j = 0; i < consecutive_read.Length; i += 2, ++j)
                {
                    eeprom[j] |= (ushort)((consecutive_read[i] << 8) | consecutive_read[i + 1]);
                }
            }
            this.eeprom.SetEEPROM(eeprom);
        }
        void Connect()
        {
            bool timed_out = true;
            for (int i = 0; i < 5; ++i)
            {
                try
                {
                    if (this.GetHardwareId().Length != 0)
                    {
                        timed_out = false;
                        break;
                    }
                }
                catch (Exception e)
                {
                    Log.Error(TAG, "Exception GetHardWareID " + e.Message);
                }
                Thread.Sleep(1000);
            }
            if (timed_out)
                throw new Exception("The command timed out while attempting to get HW id");

            this.StopRead();
            this.SetVdd(3.3);
            this.__send_buffered_command(__command_response_pairs_init_sw_i2c);
            this.__send_buffered_command(__command_response_pairs_begin_conversion);
            this.SendCommand(new byte[] { 0xAE, 0x33, 0x24, 0x00, 0x80, 0x06 });
            this.SendCommand(new byte[] { 0xAE, 0x33, 0x80, 0x00, 0x22, 0x00 });

            this.GetSensorType();
            if (this.sensor_type == 0)
            {
                this.frame_length_bytes = 32 * 26 * 2;
            }
            if (this.sensor_type == 1)
            {
                this.frame_length_bytes = 16 * 16 * 2;
            }

        }

        void SetFrameRate(double frame_rate_hz)
        {
            (byte[] ctrl_reg_1, byte status) = this.I2CRead(0x800D, 2);
            if (0 != status)
                throw new Exception("Error during read of Control register 1");
            ushort ctrl_reg_1_val = (ushort)(StructConverter.Unpack(">H", ctrl_reg_1)[0]);
            int frame_rate_code = frame_rate_to_bit_mask(frame_rate_hz);
            if (frame_rate_code < 0)
                throw new Exception(String.Format("Invalid value for frame rate: {0}", frame_rate_hz));
            else
            {
                ctrl_reg_1_val &= 0xFC7F; // clear the 3 bits that represent the frame rate
                ctrl_reg_1_val |= (ushort)(frame_rate_code << 7);
            }
            string dummy;
            status = I2CWrite(0x800D, StructConverter.Pack(new object[] { ctrl_reg_1_val }, false, out dummy));
            if (0 != status)
            {
                throw new Exception("Error during write of Control register 1");
            }
            this.SetVdd(3.3);
            if (this.support_buffer)
            {
                this.StartDataAcquisition();//frame_rate_hz);
            }
        }

        public void StartDataAcquisition(/*double frame_rate_hz*/)
        {
            double frame_rate_hz = this.frame_rate;
            int wait_us = (int)Math.Ceiling(1000000 / frame_rate_hz);
            byte[] cmd, result;
            if(this.sensor_type == 0)
            {
                cmd = Combine(CMD_StartDAQ_90640, StructConverter.Pack(new object[] { this.i2c_addr, wait_us }));
                result = SendCommand(cmd);
                if(! new byte[] { 0xAB, 0x00 }.SequenceEqual(result))
                {
                    throw new Exception("Error during execution of command on the EVB");
                }
            } else
            {
                cmd = Combine(CMD_StartDAQ_90641, StructConverter.Pack(new object[] { this.i2c_addr, wait_us }));
                result = SendCommand(cmd);
                if (!new byte[] { 0xB3, 0x00 }.SequenceEqual(result))
                {
                    throw new Exception("Error during execution of command on the EVB");
                }
            }
        }

        int frame_rate_to_bit_mask(double f_rate)
        {
            double[] frame_rates = new double[] { 0.5f, 1, 2, 4, 8, 16, 32, 64 };
            return Array.IndexOf(frame_rates, f_rate);
        }

        int GetSensorType()
        {
            (byte[] sensor, byte stat) = I2CRead(0x240A, 2);
            ushort sensor_val = (ushort)(StructConverter.Unpack(">H", sensor)[0]);
            this.sensor_type = (sensor_val & 0x40) >> 6;
            return this.sensor_type;
        }

        byte I2CWrite(ushort addr, byte[] data)
        {
            string dummy;
            byte[] cmd = Combine(CMD_I2C_Master_90640, StructConverter.Pack(new object[] { this.i2c_addr, addr }, false, out dummy));
            cmd = Combine(cmd, data);
            cmd = Combine(cmd, new byte[2] { 0, 0 });
            byte[] result = SendCommand(cmd);
            return result[1];
        }

        (byte[], byte) I2CRead(ushort addr, ushort count = 1)
        {
            string dummy;
            byte[] cmd = Combine(CMD_I2C_Master_90640, StructConverter.Pack(new object[] { this.i2c_addr, addr }, false, out dummy));
            cmd = Combine(cmd, StructConverter.Pack(new object[] { count }));
            byte[] resp = SendCommand(cmd);
            int length = resp.Length - 2;
            return (resp.Skip(2).Take(length).ToArray(), resp[1]);
        }

        byte[] SetVdd(double vdd)
        {
            byte[] cmd = Combine(CMD_SetVdd, new byte[] { 0 });
            cmd = Combine(cmd, StructConverter.Pack(new object[] { (float)vdd }));
            return SendCommand(cmd);
        }

        byte[] GetHardwareId()
        {
            return SendCommand(CMD_GetHardwareID);
        }

        void __send_buffered_command( (byte[], byte[]) name)
        {
            Send(name.Item1);
            byte[] resp = ReceiveAnswer();
            if (!name.Item2.SequenceEqual(resp))
            {
                throw new Exception(String.Format("Did not get expected response {0} != {1}", name.Item2, resp));
            }
        }

        byte[] GetCmdCrc(byte[] cmd)
        {
            uint crc = 0;
            foreach (byte i in cmd) {
                crc += i;
                if (crc > 255)
                    crc -= 255;
            }
            return Combine(cmd, new byte[] { (byte)(255 - crc) });
        }

        byte[]SendCommand(byte[] cmd)
        {
            Send(cmd);
            return ReceiveAnswer();
        }

        void Send(byte[] cmd)
        {
            int n = cmd.Length;
            if (n < 1)
                return;
            if (n > 253)
                throw new Exception("Command is limited to 253 bytes!");
            byte[] data = Combine(new byte[] { (byte)n }, GetCmdCrc(cmd));

            port.PurgeHwBuffers(true, false);
            MLX906.clearData();
            Log.Info(TAG, "Send: " + BitConverter.ToString(data));
            port.Write(data, WRITE_WAIT_MILLIS);
        }

        byte[] ReceiveAnswer()
        {
            //byte[] first = new byte[1];
            //int n = port.Read(first, READ_WAIT_MILLIS);
            //if (n != 1) {
            //    Log.Error(TAG, "Read error 1.");
            //    throw new Exception("Read Error 1");
            //}
            byte[] first = popData(1);
            ushort count;
            int crc;
            int crc_calc;
            if(first[0] == 255)
            {
                //byte[] length = new byte[2];
                //n = port.Read(length, READ_WAIT_MILLIS);
                //if (n != 2)
                //{
                //    Log.Error(TAG, "Read error 2.");
                //    throw new Exception("Read Error 2");
                //}
                byte[] length = popData(2);
                count = (ushort)StructConverter.Unpack(">H", length)[0];//(length[0] << 8) | length[1];
                crc_calc = 1;
                crc_calc += length[0];
                if (crc_calc > 255) crc_calc -= 255;
                crc_calc += length[1];
                if (crc_calc > 255) crc_calc -= 255;
            }
            else
            {
                count = first[0];
                crc_calc = 0;
            }
            //byte[] data = new byte[count+1];
            //n = port.Read(data, READ_WAIT_MILLIS);
            //if (n != data.Length) {
            //    Log.Error(TAG, "Read error 3.");
            //    throw new Exception("Read Error 3");
            //}
            byte[] data = popData(count + 1);
            crc = data[count];
            data = data.Take(count).ToArray();
            foreach (byte i in data)
            {
                crc_calc += i;
                if (crc_calc > 255)
                    crc_calc -= 255;
            }
            crc_calc = 255 - crc_calc;
            if (crc_calc != crc) {
                Log.Error(TAG, "CRC error.");
                throw new Exception("CRC Error");
            }
            //Log.Info(TAG, "Received: " + BitConverter.ToString(data.Take(10).ToArray()));
            return data;

        }
    }
}