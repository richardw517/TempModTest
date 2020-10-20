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
    interface IMlxEEPROM
    {
        int GetEEPROMSize();
        void SetEEPROM(ushort[] eeprom);
        void ReadEEPROMFromDevice();
        int GetParameterCode(int param_id, object index=null);
    }
}