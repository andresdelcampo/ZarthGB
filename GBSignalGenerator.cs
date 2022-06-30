using System;
using System.Diagnostics;
using NAudio.Wave.SampleProviders;

namespace ZarthGB
{
    public class GBSignalGenerator : SignalGenerator
    {
        private int nSample;
        private double phi;
        private string[] WaveDutyTable =
        {
            "00000001",
            "00000011",
            "00001111",
            "11111100"
        };
        private int waveDutyPosition;
        private int frequencyTimerStart;
        private int frequencyTimer;
        private double frequency;
        
        /*public new double Frequency
        {
            get => frequency;
            set
            {
                frequency = value;
                frequencyTimerStart = (2048 - (int)Frequency) * 4;
                frequencyTimerStart = 50000;
                //frequencyTimerStart *= (int) (WaveFormat.SampleRate / (4.19*1024*1024));    // 4.19 MHz
                //frequencyTimerStart = (int) ((double)frequencyTimerStart / ((double)WaveFormat.SampleRate / (4.19*1024*1024)));    // 4.19 MHz
                Debug.Print("frequencyTimerStart " + frequencyTimerStart);
                frequencyTimer = 0;
                waveDutyPosition = 0;
            }
        }*/
        
        public int WaveDuty { get; set; }

        public GBSignalGenerator() : base (44100, 1)
        {
        }
        
        public new int Read(float[] buffer, int offset, int count)
        {
            int num1 = offset;
            for (int index1 = 0; index1 < count / WaveFormat.Channels; ++index1)
            {
                double num2;
                switch (Type)
                { 
                    /*case SignalGeneratorType.Sweep:
                        phi += 2.0 * Math.PI * Math.Exp(FrequencyLog + (double) nSample * (FrequencyEndLog - FrequencyLog) / (SweepLengthSecs * (double) WaveFormat.SampleRate)) / (double) WaveFormat.SampleRate;
                        num2 = Gain * Math.Sin(phi);
                        ++nSample;
                        if ((double) nSample > SweepLengthSecs * (double) WaveFormat.SampleRate)
                        {
                            nSample = 0;
                            phi = 0.0;
                            break;
                        }
                        break;*/
                    case SignalGeneratorType.Square:
                        num2 = (double) nSample * (2.0 * Frequency / (double) WaveFormat.SampleRate) % 2.0 - 1.0 >= 0.0 ? Gain : -Gain;
                        /*if (frequencyTimer == 0)
                        {
                            waveDutyPosition = (waveDutyPosition + 1) % WaveDutyTable[WaveDuty].Length;
                            frequencyTimer = frequencyTimerStart;
                        }
                        else
                            frequencyTimer--;
                        num2 = (WaveDutyTable[WaveDuty][waveDutyPosition] == '1') ? Gain : -Gain;
                        */
                        nSample++;
                        break;
                    default:
                        num2 = 0.0;
                        break;
                }
                
                for (int index2 = 0; index2 < WaveFormat.Channels; ++index2)
                  buffer[num1++] = !PhaseReverse[index2] ? (float) num2 : (float) -num2;
            }
            return count;
        }
    }
}