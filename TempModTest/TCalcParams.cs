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
    class TCalcParams
    {
        public const int MAX_IR_COLS = 32;
        public const int MAX_IR_ROWS = 24;
        public const int MAX_IR_PIXELS = MAX_IR_COLS * MAX_IR_ROWS;
        public const int MAX_IR_COLS_90641 = 16;
        public const int MAX_IR_ROWS_90641 = 12;
        public const int MAX_IR_PIXELS_90641 = MAX_IR_COLS_90641 * MAX_IR_ROWS_90641;
        public const int MAX_CAL_RANGES = 4;
        public const int NUM_TGC = 2;
        public const int NUM_PAGES = 2;

        public int version;
        public int Id0;
        public int Id1;
        public int Id2;
        public double Vdd_25;
        public double Kv_Vdd;
        public double Res_scale;
        public double VPTAT_25;
        public double Kv_PTAT;
        public double Kt_PTAT;
        public double alpha_ptat;
        public double GainMeas_25_3v2;
        public double[][] Pix_os_ref;
        public double[][] Pix_os_ref_SP0;
        public double[][] Pix_os_ref_SP1;
        public double[][] Kta;
        public double[][] Kv;
        public double[] alpha;

        public double Vdd_V0;
        public double[] Ta_min;
        public double[] Ta_max;
        public double[] Ta0;
        public double[] TGC;
        public double[][][] Pix_os_ref_TGC;
        public double[][][] Kta_TGC;
        public double[][][] Kv_TGC;
        public double[][] alpha_TGC;
        public double KsTa;
        public double Ta_0_Alpha;
        public double KsTo;
        public double To_0_Alpha;

        public TCalcParams()
        {
            this.version = 0;
            this.Id0 = 0;
            this.Id1 = 0;
            this.Id2 = 0;                         // Two 32-bit numbers for chip ID
            this.Vdd_25 = 0;                      // LSB16,Value of VDD measurement at 25 degC and 3.2V supply
            this.Kv_Vdd = 0;                      // LSB16/V,slope of VDD measurements
            this.Res_scale = 0;                   // V/V,Scaling coefficient of #resolution
            this.VPTAT_25 = 0;                    // LSB18,VPTAT value for 5 degC and Vdd=3.2V
            this.Kv_PTAT = 0;                     // LSB18/V,Supply dependence of VPTAT
            this.Kt_PTAT = 0;                     // LSB18/degC,Slope of PTAT
            this.alpha_ptat = 0;                  // V/V,Virtual reference coefficient
            this.GainMeas_25_3v2 = 0;             // LSB,Gain measurement channel for 3.2V supply and 25 degC
            this.Pix_os_ref = new double[MAX_CAL_RANGES][];
            for (int i = 0; i < MAX_CAL_RANGES; ++i)
            {
                this.Pix_os_ref[i] = new double[MAX_IR_PIXELS];
                for (int j = 0; j < MAX_IR_PIXELS; ++j)
                    this.Pix_os_ref[i][j] = 0.0;
            }
            this.Pix_os_ref_SP0 = new double[MAX_CAL_RANGES][];
            for (int i = 0; i < MAX_CAL_RANGES; ++i)
            {
                this.Pix_os_ref_SP0[i] = new double[MAX_IR_PIXELS_90641];
                for (int j = 0; j < MAX_IR_PIXELS_90641; ++j)
                    this.Pix_os_ref_SP0[i][j] = 0.0;
            }
            this.Pix_os_ref_SP1 = new double[MAX_CAL_RANGES][];
            for (int i = 0; i < MAX_CAL_RANGES; ++i)
            {
                this.Pix_os_ref_SP1[i] = new double[MAX_IR_PIXELS_90641];
                for (int j = 0; j < MAX_IR_PIXELS_90641; ++j)
                    this.Pix_os_ref_SP1[i][j] = 0.0;
            }
            this.Kta = new double[MAX_CAL_RANGES][];
            for (int i = 0; i < MAX_CAL_RANGES; ++i)
            {
                this.Kta[i] = new double[MAX_IR_PIXELS];
                for (int j = 0; j < MAX_IR_PIXELS; ++j)
                    this.Kta[i][j] = 0.0;
            }
            this.Kv = new double[MAX_CAL_RANGES][];
            for (int i = 0; i < MAX_CAL_RANGES; ++i)
            {
                this.Kv[i] = new double[MAX_IR_PIXELS];
                for (int j = 0; j < MAX_IR_PIXELS; ++j)
                    this.Kv[i][j] = 0.0;
            }
            this.alpha = new double[MAX_IR_PIXELS];
            for (int j = 0; j < MAX_IR_PIXELS; ++j)
                this.alpha[j] = 1.0;

            this.Vdd_V0 = 0;
            this.Ta_min = new double[MAX_CAL_RANGES];
            for (int j = 0; j < MAX_CAL_RANGES; ++j)
                this.Ta_min[j] = 0.0;

            this.Ta_max = new double[MAX_CAL_RANGES];
            for (int j = 0; j < MAX_CAL_RANGES; ++j)
                this.Ta_max[j] = 0.0;

            this.Ta0 = new double[MAX_CAL_RANGES];
            for (int j = 0; j < MAX_CAL_RANGES; ++j)
                this.Ta0[j] = 0.0;

            this.TGC = new double[NUM_TGC];
            for (int j = 0; j < NUM_TGC; ++j)
                this.TGC[j] = 0.0;

            this.Pix_os_ref_TGC = new double[MAX_CAL_RANGES][][];
            for (int i = 0; i < MAX_CAL_RANGES; ++i)
            {
                this.Pix_os_ref_TGC[i] = new double[NUM_PAGES][];
                for (int p = 0; p < NUM_PAGES; ++p)
                {
                    this.Pix_os_ref_TGC[i][p] = new double[NUM_TGC];
                    for (int j = 0; j < NUM_TGC; ++j)
                        this.Pix_os_ref_TGC[i][p][j] = 0.0;
                }
            }

            this.Kta_TGC = new double[MAX_CAL_RANGES][][];
            for (int i = 0; i < MAX_CAL_RANGES; ++i)
            {
                this.Kta_TGC[i] = new double[NUM_PAGES][];
                for (int p = 0; p < NUM_PAGES; ++p)
                {
                    this.Kta_TGC[i][p] = new double[NUM_TGC];
                    for (int j = 0; j < NUM_TGC; ++j)
                        this.Kta_TGC[i][p][j] = 0.0;
                }
            }

            this.Kv_TGC = new double[MAX_CAL_RANGES][][];
            for (int i = 0; i < MAX_CAL_RANGES; ++i)
            {
                this.Kv_TGC[i] = new double[NUM_PAGES][];
                for (int p = 0; p < NUM_PAGES; ++p)
                {
                    this.Kv_TGC[i][p] = new double[NUM_TGC];
                    for (int j = 0; j < NUM_TGC; ++j)
                        this.Kv_TGC[i][p][j] = 0.0;
                }
            }

            this.alpha_TGC = new double[NUM_PAGES][];
            for (int i = 0; i < NUM_PAGES; ++i)
            {
                this.alpha_TGC[i] = new double[NUM_TGC];
                for (int j = 0; j < NUM_TGC; ++j)
                    this.alpha_TGC[i][j] = 0.0;
            }

            this.KsTa = 0;
            this.To_0_Alpha = 0;
            this.KsTo = 0;
            this.To_0_Alpha = 0;

            this.SetDefaults();
        }

        void SetDefaults()
        {
            this.version = 0;
            this.Id0 = 0;
            this.Id1 = 0;
            this.Id2 = 0;
            this.Vdd_25 = -19474;
            this.Kv_Vdd = -4690;
            this.Res_scale = 8;
            this.VPTAT_25 = 10974;
            this.Kv_PTAT = 0.0113;
            this.Kt_PTAT = 35.74;
            this.alpha_ptat = 11.2;
            this.GainMeas_25_3v2 = 5471;

            this.Vdd_V0 = 3.3;
            this.KsTa = 0.001;
            this.Ta_0_Alpha = 25.0;
            this.KsTo = 0.0004;
            this.To_0_Alpha = 0.0;

            double[] arrTa_min = new double[] { -40.0, 70.0, 110.0, 900 };
            double[] arrTa_max = new double[] { 70.0, 110.0, 150.0, 800 };
            double[] arrTa0 = new double[] { 25.0, 90.0, 130.0, 900 };

            for(int t = 0; t < MAX_CAL_RANGES; ++t)
            {
                this.Ta_min[t] = arrTa_min[t];
                this.Ta_max[t] = arrTa_max[t];
                this.Ta0[t] = arrTa0[t];
                for(int i = 0; i < MAX_IR_PIXELS; ++i)
                {
                    this.Pix_os_ref[t][i] = 0;
                    this.Kta[t][i] = 0;
                    this.Kv[t][i] = 0;
                }
            }

            for(int i = 0; i < MAX_IR_PIXELS; ++i)
            {
                this.alpha[i] = 1;
            }

            for (int i = 0; i < NUM_TGC; ++i)
            {
                this.TGC[i] = 0;
                for(int page = 0; page < NUM_PAGES; ++page)
                {
                    this.alpha_TGC[page][i] = 1;
                    for (int t = 0; t < MAX_CAL_RANGES; ++t)
                    {
                        this.Pix_os_ref_TGC[t][page][i] = 0;
                        this.Kta_TGC[t][page][i] = 0;
                        this.Kv_TGC[t][page][i] = 0;
                    }
                }
            }
        }
    }
}