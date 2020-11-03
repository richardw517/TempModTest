using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Android.App;
using Android.Content;
using Android.Graphics;
using Android.OS;
using Android.Runtime;
using Android.Util;
using Android.Views;
using Android.Widget;

namespace TempModTest_MLX906
{
    class GraphView : View
    {
        const int ColorLevels = 32;

        private Paint[] paints;
        private Paint paintStroke;
        private Rect rect = new Rect();

        private void init()
        {
            paintStroke = new Paint();
            paintStroke.Color = Color.Black;
            paintStroke.SetStyle(Paint.Style.Stroke);

            paints = new Paint[ColorLevels];
            byte interval = 256 * 4 / (ColorLevels);
            Color color = Color.Rgb(0, 0, 255);
            for(int i = 0; i < ColorLevels; ++i)
            {
                paints[i] = new Paint();
                paints[i].SetStyle(Paint.Style.Fill);
                paints[i].Color = new Color(color);
                if (i < ColorLevels / 4)
                {
                    color.G += interval;
                    if (color.G == 0)
                        color.G = 255;
                } else if (i < ColorLevels / 2)
                {
                    if (color.B > interval)
                        color.B -= interval;
                    else
                        color.B = 0;
                } else if (i < ColorLevels * 3 / 4)
                {
                    color.R += interval;
                    if (color.R == 0)
                        color.R = 255;
                } else
                {
                    if (color.G > interval)
                        color.G -= interval;
                    else
                        color.G = 0;
                }
            }
            //byte interval = 256 * 2 / (ColorLevels);
            //Color color = Color.Rgb(0, 0, 255);
            //for (int i = 0; i < ColorLevels; ++i)
            //{
            //    paints[i] = new Paint();
            //    paints[i].SetStyle(Paint.Style.Fill);
            //    paints[i].Color = new Color(color);
            //    if (color.B > interval)
            //    {
            //        color.R = 0;
            //        color.B -= interval;
            //        color.G += interval;
            //        if (color.G == 0)
            //            color.G = 255;
            //    }
            //    else
            //    {
            //        color.B = 0;
            //        if (color.G > interval)
            //            color.G -= interval;
            //        else
            //            color.G = 0;
            //        color.R += interval;
            //        if (color.R == 0)
            //            color.R = 255;
            //    }

            //    /*
            //    if (i < ColorLevels / 2)
            //    {
            //        color.B -= interval;
            //        color.G += (byte)(2*interval);
            //        if (color.G == 0)
            //            color.G = 255;
            //        color.R = 0;
            //    }
            //    else
            //    {
            //        color.B = 0;
            //        if (color.G > 2 * interval)
            //            color.G -= (byte)(2 * interval);
            //        else
            //            color.G = 0;
            //        if (color.R == 0)
            //            color.R = 128;
            //        else
            //            color.R += interval;
            //    }*/
            //}
        }

        public GraphView(Context context) : base(context)
        {
        }

        public GraphView(Context context, IAttributeSet attrs) : base(context, attrs)
        {
        }

        public GraphView(Context context, IAttributeSet attrs, int defStyleAttr) : base(context, attrs, defStyleAttr)
        {
        }

        public GraphView(Context context, IAttributeSet attrs, int defStyleAttr, int defStyleRes) : base(context, attrs, defStyleAttr, defStyleRes)
        {
        }

        protected GraphView(IntPtr javaReference, JniHandleOwnership transfer) : base(javaReference, transfer)
        {
        }

        public double[] Data { get; set; }

        protected override void OnDraw(Canvas canvas)
        {
            base.OnDraw(canvas);            

            if(Data == null)
            {
                Paint paint = new Paint();
                paint.SetStyle(Paint.Style.Fill);
                paint.Color = Color.LightGray;
                rect.Top = 0;
                rect.Left = 0;
                rect.Right = canvas.Width;
                rect.Bottom = canvas.Height;
                canvas.DrawRect(rect, paint);
            } else
            {
                if (paints == null)
                    init();

                int xSize, ySize;
                if(Data.Length >= 32 * 24)
                {
                    xSize = 32;
                    ySize = 24;
                }
                else
                {
                    xSize = 16;
                    ySize = 12;
                }
                double minVal = Data.Min();
                double maxVal = Data.Max();
                double range = maxVal - minVal;
                double avg = Data.Average();

                int width = canvas.Width;
                int height = canvas.Height;
                int startX = 0;
                int startY = 0;
                int squareSize;
                if(width * ySize > height * xSize)
                {
                    squareSize = height / ySize;

                } else
                {
                    squareSize = width / xSize;
                }
                int correctHeight = squareSize * ySize;
                startY = (height - correctHeight) / 2;
                int correctWidth = squareSize * xSize;
                startX = (width - correctWidth) / 2;

                for (int y = startY, j = 0; j < ySize; ++j, y += squareSize)
                {
                    for (int x = startX, i = 0; i < xSize; ++i, x += squareSize)
                    {
                        double t = Data[j * xSize + i];
                        int c;
                        c = (int)((t - minVal) * ColorLevels / (maxVal - minVal));
                        if (c == ColorLevels)
                            c--;

                        /*if (t > avg)
                        {
                            c = ColorLevels / 2 + (int)((t - avg) * (ColorLevels / 2) / (maxVal - avg));
                            if (c == ColorLevels)
                                c--;
                        } else
                        {
                            c = (int)((t - minVal) * (ColorLevels / 2) / (avg - minVal));
                        }*/
                        rect.Top = y;
                        rect.Left = x;
                        rect.Right = x + squareSize;
                        rect.Bottom = y + squareSize;

                        canvas.DrawRect(rect, paints[c]);
                        if (t == maxVal)
                        {
                            rect.Top++;
                            rect.Left++;
                            rect.Right--;
                            rect.Bottom--;
                            canvas.DrawRect(rect, paintStroke);
                        }
                    }
                }
            }
            
        }
    }
}