using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Android.App;
using Android.Content;
using Android.OS;
using Android.Runtime;
using Android.Views;
using Android.Widget;

namespace TempModTest_MLX906
{
    class Mlx90640EEPROM : IMlxEEPROM
    {
        static Dictionary<int, object> eeprom_map = new Dictionary<int, object>()
        {
            { ParameterCodesEEPROM.CodeOscTrim, 0 },
            { ParameterCodesEEPROM.CodeAnalogTrim, 1 },
            { ParameterCodesEEPROM.CodeConfiguration, 3 },
            { ParameterCodesEEPROM.CodeI2CAddr, 4 },
            //{ ParameterCodesEEPROM.CodeAnalogTrim2, null },
            { ParameterCodesEEPROM.CodeCropPageAddr, 5 },
            { ParameterCodesEEPROM.CodeCropCellAddr, 6 },
            { ParameterCodesEEPROM.CodeID1, 7 },
            { ParameterCodesEEPROM.CodeID2, 8 },
            { ParameterCodesEEPROM.CodeID3, 9 },
            { ParameterCodesEEPROM.CodeDeviceOptions, 10 },
            { ParameterCodesEEPROM.CodeControl1, 0xC },
            { ParameterCodesEEPROM.CodeControl2, 0xD },
            { ParameterCodesEEPROM.CodeI2CConf, 0xE },
            //#  ParameterCodesEEPROM.CodeEepromIdVersion: -1,
            //#  ParameterCodesEEPROM.CodeArraySize: -1,
            { ParameterCodesEEPROM.CodeScale_Occ_rem, new int[]{ 0x10, 0, 4 } },
            { ParameterCodesEEPROM.CodeScale_Occ_col, new int[]{ 0x10, 4, 4 } },
            { ParameterCodesEEPROM.CodeScale_Occ_row, new int[]{ 0x10, 8, 4 } },
            { ParameterCodesEEPROM.CodeAlpha_PTAT, new int[]{ 0x10, 12, 4} },
            { ParameterCodesEEPROM.CodePix_os_average, new int[]{ 0x11, 0, 16} },
            { ParameterCodesEEPROM.CodeScale_Acc_rem, new int[]{ 0x20, 0, 4} },
            { ParameterCodesEEPROM.CodeScale_Acc_col, new int[]{ 0x20, 4, 4} },
            { ParameterCodesEEPROM.CodeScale_Acc_row, new int[]{ 0x20, 8, 4} },
            { ParameterCodesEEPROM.CodeAlpha_scale, new int[]{ 0x20, 12, 4} },
            { ParameterCodesEEPROM.CodePix_sens_average, new int[]{ 0x21, 0, 16 } },
            { ParameterCodesEEPROM.CodeGAIN, new int[]{ 0x30, 0, 16} },
            { ParameterCodesEEPROM.CodePTAT_25, new int[]{ 0x31, 0, 16} },
            { ParameterCodesEEPROM.CodeKt_PTAT, new int[]{ 0x32, 0, 10 } },
            { ParameterCodesEEPROM.CodeKv_PTAT, new int[]{ 0x32, 10, 6 } },
            { ParameterCodesEEPROM.CodeVdd_25, new int[]{ 0x33, 0, 8 } },
            { ParameterCodesEEPROM.CodeK_Vdd, new int[]{ 0x33, 8, 8 } },
            { ParameterCodesEEPROM.CodeKv_CP, new int[]{ 0x3B, 8, 8} },
            { ParameterCodesEEPROM.CodeRes_control, new int[]{ 0x38, 12, 2 } },
            { ParameterCodesEEPROM.CodeKsTo_R3, new int[]{ 0x3E, 0, 8 } },
            { ParameterCodesEEPROM.CodeCT2, new int[]{ 0x3F, 8, 4 } },
            { ParameterCodesEEPROM.CodeKv_Avg_RO_CO, new int[]{0x34, 12, 4}},
            { ParameterCodesEEPROM.CodeKta_scale2, new int[]{ 0x38, 0, 4 } },
            { ParameterCodesEEPROM.CodeCT1, new int[]{ 0x3F, 4, 4 } },
            { ParameterCodesEEPROM.CodeOffset_CP_P1_P0, new int[]{ 0x3A, 10, 6 } },
            { ParameterCodesEEPROM.CodeTemp_Step, new int[]{ 0x3F, 12, 2 } },
            { ParameterCodesEEPROM.CodeKsTo_R1, new int[]{ 0x3D, 0, 8 } },
            { ParameterCodesEEPROM.CodeKv_scale, new int[]{ 0x38, 8, 4 } },
            { ParameterCodesEEPROM.CodeKta_scale1, new int[]{ 0x38, 4, 4 } },
            { ParameterCodesEEPROM.CodeKsTo_R4, new int[]{ 0x3E, 8, 8 } },
            { ParameterCodesEEPROM.CodeKv_Avg_RO_CE, new int[]{ 0x34, 4, 4 } },
            { ParameterCodesEEPROM.CodeOffset_CP_P0, new int[]{ 0x3A, 0, 10 } },
            { ParameterCodesEEPROM.CodeKta_Avg_RO_CO, new int[]{ 0x36, 8, 8 } },
            { ParameterCodesEEPROM.CodeAlpha_CP_P0, new int[]{ 0x39, 0, 10 } },
            { ParameterCodesEEPROM.CodeKsTo_R2, new int[]{ 0x3D, 8, 8 } },
            { ParameterCodesEEPROM.CodeKta_Avg_RE_CO, new int[]{ 0x36, 0, 8 } },
            { ParameterCodesEEPROM.CodeScale_KsTo, new int[]{ 0x3F, 0, 4 } },
            { ParameterCodesEEPROM.CodeKv_Avg_RE_CO, new int[]{ 0x34, 8, 4 } },
            { ParameterCodesEEPROM.CodeTGC, new int[]{ 0x3C, 0, 8 } },
            { ParameterCodesEEPROM.CodeKta_Avg_RE_CE, new int[]{ 0x37, 0, 8 } },
            { ParameterCodesEEPROM.CodeAlpha_CP_P1_P0, new int[]{ 0x39, 10, 6 } },
            { ParameterCodesEEPROM.CodeKta_Avg_RO_CE, new int[]{ 0x37, 8, 8 } },
            { ParameterCodesEEPROM.CodeKsTa, new int[]{ 0x3C, 8, 8 } },
            { ParameterCodesEEPROM.CodeKta_CP, new int[]{ 0x3B, 0, 8 } },
            { ParameterCodesEEPROM.CodeKv_Avg_RE_CE, new int[]{ 0x34, 0, 4 } },
            { ParameterCodesEEPROM.CodeOCC_row, ( 0, 24,  new Func<int, int[]>( (int index) => new int[] { 0x12 + (index / 4), index % 4 * 4, 4 } ) ) },
            { ParameterCodesEEPROM.CodeOCC_column, (0, 32, new Func<int, int[]>( (int index) => new int[] { 0x18 + index / 4, (index % 4) * 4, 4 } ) ) },
            { ParameterCodesEEPROM.CodeACC_row, (0, 24, new Func<int, int[]>( (int index) => new int[] { 0x22 + index / 4, (index % 4) * 4, 4 } ) ) },
            { ParameterCodesEEPROM.CodeACC_column, (0, 32, new Func<int, int[]>( (int index) => new int[] { 0x28 + index / 4, (index % 4) * 4, 4 } ) ) },
            { ParameterCodesEEPROM.CodePixel_Kta, (0, 32 * 24, new Func<int, int[]>( (int index) => new int [] { 0x40 + index, 1, 3 } ) ) },
            { ParameterCodesEEPROM.CodePixel_Alpha, (0, 32 * 24, new Func<int, int[]>( (int index) => new int[] { 0x40 + index, 4, 6 } ) ) },
            { ParameterCodesEEPROM.CodePixel_Offset, (0, 32 * 24, new Func<int, int[]>( (int index) => new int[] { 0x40 + index, 10, 6 } ) ) },
        };

        MLX906 device;
        int eeprom_size;
        ushort[] eeprom;

        int IMlxEEPROM.GetEEPROMSize()
        {
            return eeprom_size;
        }

        void IMlxEEPROM.SetEEPROM(ushort[] eeprom)
        {
            this.eeprom = eeprom;
        }

        public Mlx90640EEPROM(MLX906 device)
        {
            this.device = device;
            this.eeprom = null;
            this.eeprom_size = 0x680;
        }

        bool GetBit(int index, int lsb)
        {
            return (this.eeprom[index] & (1 << lsb)) != 0;
        }

        int GetBits(int index, int lsb, int nbits)
        {
            return (this.eeprom[index] >> lsb) & ((1 << nbits) - 1);
        }

        void SetBits(int index, int data, int lsb, int nbits)
        {
            int mask = (1 << nbits) - 1;
            this.eeprom[index] = (ushort)((this.eeprom[index] & ~(mask << lsb)) | ((data & mask) << lsb));
        }

        int GetEEPROMIdVersion()
        {
            if ((this.eeprom[0x09] == 0x100C && this.eeprom[0x0A] == 0x0020) || (this.eeprom[0x09] == 0 && this.eeprom[0x0A] == 0))
                return 0;
            else
                return 1;
        }

        void IMlxEEPROM.ReadEEPROMFromDevice()
        {
            this.device.ReadEEPromFromDevice();
        }

        int IMlxEEPROM.GetParameterCode(int param_id, object index)
        {
            if (this.eeprom == null)
                throw new Exception("EEPROM is not read from device");
            object par = Mlx90640EEPROM.eeprom_map[param_id];
            if (par is int)
                return this.eeprom[(int)par];
            else if (typeof(int[]).Equals(par.GetType()))
            {
                int[] arr = (int[])par;
                return this.GetBits(arr[0], arr[1], arr[2]);
            }
            else if (par is ValueTuple<int, int, Func<int, int[]>>)
            {
                ValueTuple<int, int, Func<int, int[]>> t = (ValueTuple<int, int, Func<int, int[]>>)par;
                if (index == null)
                    throw new Exception("For this parameter index can not be None");
                int idx = (int)index;
                if (t.Item1 <= idx && idx < t.Item2)
                {
                    int[] cal = t.Item3(idx);
                    return this.GetBits(cal[0], cal[1], cal[2]);
                }
                else
                {
                    throw new Exception(String.Format("index: {0} out of range ({1}, {2})", idx, t.Item1, t.Item2));
                }
            }
            else
            {
                throw new Exception(String.Format("invalid eeprom parameter at {0}", param_id));
            }
        }
    }
}