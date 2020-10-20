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
    class Mlx90641EEPROM : IMlxEEPROM
    {
        static Dictionary<int, object> eeprom_map = new Dictionary<int, object>()
        {
            { ParameterCodesEEPROM.CodeOscTrim, 0 },
            { ParameterCodesEEPROM.CodeAnalogTrim, 1 },
            { ParameterCodesEEPROM.CodeConfiguration, 3 },
            { ParameterCodesEEPROM.CodeI2CAddr, 0xF },  //# (ChipV == ChipVersion90640AAA || ChipV == ChipVersion90641AAA)
            { ParameterCodesEEPROM.CodeAnalogTrim2, 4 }, //# (ChipV == ChipVersion90640AAA || ChipV == ChipVersion90641AAA)
            { ParameterCodesEEPROM.CodeCropPageAddr, 5 },
            { ParameterCodesEEPROM.CodeCropCellAddr, 6 },
            { ParameterCodesEEPROM.CodeID1, 7 },
            { ParameterCodesEEPROM.CodeID2, 8 },
            { ParameterCodesEEPROM.CodeID3, 9 },
            { ParameterCodesEEPROM.CodeDeviceOptions, 10 },
            { ParameterCodesEEPROM.CodeControl1, 0xC },
            { ParameterCodesEEPROM.CodeControl2, 0xD },
            { ParameterCodesEEPROM.CodeI2CConf, 0xE },
            { ParameterCodesEEPROM.CodeScale_occ_os, new int[] { 0x10, 6, 6 } },
            { ParameterCodesEEPROM.CodePix_os_average, (0, 2, new Func<int, int[]>( (int index) => new int[] { 0x11 + index, 0, 11 } ) ) },
            { ParameterCodesEEPROM.CodeKta_avg, new int[] { 0x15, 0, 11 } },
            { ParameterCodesEEPROM.CodeKta_scale_2, new int[] { 0x16, 0, 5 } },
            { ParameterCodesEEPROM.CodeKta_scale_1, new int[] { 0x16, 5, 6 } },
            { ParameterCodesEEPROM.CodeKv_avg, new int[] { 0x17, 0, 11 } },
            { ParameterCodesEEPROM.CodeKv_scale_2, new int[] { 0x18, 0, 5 } },
            { ParameterCodesEEPROM.CodeKv_scale_1, new int[] { 0x18, 5, 6 } },
            { ParameterCodesEEPROM.CodeScale_row, (0,6, new Func<int, int[]>( (int index) => new int[] { 0x19 + index / 2, Math.Abs((index%2)-1)*5, Math.Abs((index%2)-1)+5 } ) ) },
            { ParameterCodesEEPROM.CodeRow_max, (0, 6, new Func<int, int[]>( (int index) => new int[] { 0x1C + index, 0, 11 } ) ) },
            { ParameterCodesEEPROM.CodeKsTa, new int[] { 0x22, 0, 11 } },
            { ParameterCodesEEPROM.CodeEmissivity, new int[] { 0x23, 0, 11 } },
            { ParameterCodesEEPROM.CodeGAIN, (0, 2, new Func<int, int[]>( (int index) => new int[] { 0x24 + index, 0, 11 } ) ) },
            { ParameterCodesEEPROM.CodeVdd_25, new int[] { 0x26, 0, 11 } },
            { ParameterCodesEEPROM.CodeK_Vdd, new int[] { 0x27, 0, 11 } },
            { ParameterCodesEEPROM.CodePTAT_25, (0, 2, new Func<int, int[]>( (int index) => new int[] { 0x28 + index, 0, 11 } ) ) },
            { ParameterCodesEEPROM.CodeKt_PTAT, new int[] { 0x2A, 0, 11 } },
            { ParameterCodesEEPROM.CodeKv_PTAT, new int[] { 0x2B, 0, 11 } },
            { ParameterCodesEEPROM.CodeAlpha_PTAT, new int[] { 0x2C, 0, 11 } },
            { ParameterCodesEEPROM.CodeAlpha_CP, new int[] { 0x2D, 0, 11 } },
            { ParameterCodesEEPROM.CodeAlpha_CP_scale, new int[] { 0x2E, 0, 11 } },
            { ParameterCodesEEPROM.CodeOffset_CP, (0, 2, new Func<int, int[]>( (int index) => new int[] { 0x2F + index, 0, 11 } ) ) },
            { ParameterCodesEEPROM.CodeKta_CP, new int[] { 0x31, 0, 6 } },
            { ParameterCodesEEPROM.CodeKta_CP_scale, new int[] { 0x31, 6, 5 } },
            { ParameterCodesEEPROM.CodeKv_CP, new int[] { 0x32, 0, 6 } },
            { ParameterCodesEEPROM.CodeKv_CP_scale, new int[] { 0x32, 6, 5 } },
            { ParameterCodesEEPROM.CodeTGC, new int[] { 0x33, 0, 9 } },
            { ParameterCodesEEPROM.CodeCalib_res_cont, new int[] { 0x33, 9, 2 } },
            { ParameterCodesEEPROM.CodeKsTo_scale, new int[] { 0x34, 0, 11 } },
            { ParameterCodesEEPROM.CodeKsTo_R1, new int[] { 0x35, 0, 11 } },
            { ParameterCodesEEPROM.CodeKsTo_R2, new int[] { 0x36, 0, 11 } },
            { ParameterCodesEEPROM.CodeKsTo_R3, new int[] { 0x37, 0, 11 } },
            { ParameterCodesEEPROM.CodeKsTo_R4, new int[] { 0x39, 0, 11 } },
            //#ParameterCodesEEPROM.CodeKsTo_R5: [0x39, 0, 11],
            //#ParameterCodesEEPROM.CodeCT6: [0x3A, 0, 11],
            //#ParameterCodesEEPROM.CodeKsTo_R6: [0x3B, 0, 11],
            //#ParameterCodesEEPROM.CodeCT7: [0x3C, 0, 11],
            //#ParameterCodesEEPROM.CodeKsTo_R7: [0x3D, 0, 11],
            //#ParameterCodesEEPROM.CodeCT8: [0x3E, 0, 11],
            //#ParameterCodesEEPROM.CodeKsTo_R8: [0x3F, 0, 11],
            { ParameterCodesEEPROM.CodePixel_Offset, (0, 16 * 12, new Func<int, int[]>( (int index) => new int[] { 0x40 + index, 0, 11 } ) ) },
            { ParameterCodesEEPROM.CodePixel_Sensitivity, (0, 16 * 12, new Func<int, int[]>( (int index) => new int[] { 0x100 + index, 0, 11 } ) ) },
            { ParameterCodesEEPROM.CodePixel_Kv, (0, 16 * 12, new Func<int, int[]>( (int index) => new int[] { 0x1C0 + index, 0, 5 } ) ) },
            { ParameterCodesEEPROM.CodePixel_Kta, (0, 16 * 12, new Func<int, int[]>( (int index) => new int[] { 0x1C0 + index, 5, 6 } ) ) },
            { ParameterCodesEEPROM.CodePixel_os, (0, 16 * 12, new Func<int, int[]>( (int index) => new int[] { 0x280 + index, 0, 11 } ) ) },
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

        public Mlx90641EEPROM(MLX906 device)
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
            object par = Mlx90641EEPROM.eeprom_map[param_id];
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