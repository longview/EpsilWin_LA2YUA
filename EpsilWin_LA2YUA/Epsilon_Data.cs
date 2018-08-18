using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.IO.Ports;
using System.IO;
using System.Device.Location;

namespace EpsilWin_LA2YUA
{
    public partial class Form1
    {
        /*  
         * Data structures for Epsilon Clock interface 
         */

            // byte class enum containing a friendly code name for each command, value = command/query ID
        public enum EpsilonCommandsIndex : byte
        {
            TOD_Format_1 = 193,
            TOD_Format_2 = 194,
            TOD_Format_3 = 195,
            TOD_Format_4 = 196,
            TOD_Format_5 = 197,

            Status_Read = 80,
            TOD_Setup_Read = 65,
            TOD_Period_Read = 66,
            GPS_Init_Read = 74,
            Local_Time_Read = 71,
            Phase_Correction_Read = 72,
            Leap_Second_Read = 73,
            Display_Read = 77,
            Alarm_Limits_Read = 78,
            Version_Read = 67,
            Force_Holdover_Read = 79,
            Manual_Time_Read = 81,
            Manual_Second_Read = 85,
            Manual_1PPS_Read = 83,
            Remote_Control_Read = 82,

            TOD_Setup_Write = 1,
            TOD_Period_Write = 2,
            
            GPS_Init_Write = 10,
            Local_Time_Write = 7,
            Phase_Correction_Write = 8,
            Leap_Second_Write = 9,
            Display_Write = 13,
            Alarm_Limit_Write = 14,
            Version_Write = 3,
            Force_Holdover_Write = 15,
            Manual_Time_Write = 17,
            Manual_Second_Write = 21,
            Manual_1PPS_Write = 19,
            Remote_Control_Write = 18,

            Error_Code = 64,
            Reset_Clock_Write = 16,
            UNKNOWN_COMMAND = 255,
            DECODE_FAILED = 254,

            // documented in series 1 manual
            GPS_Init_Time_Read = 68,
            GPS_Init_Time_Write = 4,

            // documented in series 3 manual
            Manual_Frequency_Write = 20,
            Manual_Frequency_Read = 85
        };

        public enum EpsilonCommandAccessType
        {
            Read,
            Write,
            N_A
        };

        // special conditions for allowing a write
        public enum EpsilonCommandWriteConditions
        {
            Always_Allowed,
            Remote_Must_Be_Set, // most commands
            Remote_No_Holdover, // only when not in holdover
            Remote_Only_Holdover // only available in forced holdover

        }

        public enum EpsilonCommandResponseType
        {
            Command_ACK,
            Query_Response,
            NACK,
            N_A
        };

        public class EpsilonCommandInfo
        {
            public EpsilonCommandsIndex Command_Index;
            public byte Payload_Size;
            public string FriendlyName; // name for error reporting, ACK etc.
            public EpsilonCommandAccessType ReadWrite;
            public EpsilonCommandWriteConditions Write_Conditions;
            public EpsilonCommandResponseType Command_Response_Type;

            public EpsilonCommandInfo()
            {
                Command_Response_Type = EpsilonCommandResponseType.N_A;
            }
        }

        public class EpsilonLocalTimeOffset
        {
            public EpsilonCommandsIndex ReadCommand;
            public EpsilonCommandsIndex WriteCommand;

            public sbyte Minute_Offset;
            public sbyte Hours_Offset;
            public bool DataValid;

            public EpsilonLocalTimeOffset()
            {
                ReadCommand = EpsilonCommandsIndex.Local_Time_Read;
                WriteCommand = EpsilonCommandsIndex.Leap_Second_Write;
            }

            public bool Serialize(out List<byte> payload)
            {
                bool retval = true;

                payload = new List<byte>(2);

                // ugly stuff.
                payload.Add(unchecked((byte)((sbyte)Hours_Offset)));
                payload.Add(unchecked((byte)((sbyte)Minute_Offset)));

                return retval;

            }

            public bool ProcessMessage(EpsilonSerialMessage currentmessage)
            {
                if (currentmessage.MessageID != (byte)ReadCommand)
                {
                    return false;
                }
                DataValid = true;

                sbyte c;

                c = unchecked((sbyte)currentmessage.Payload[0]);
                Hours_Offset = c;

                c = unchecked((sbyte)currentmessage.Payload[1]);
                Minute_Offset = c;

                return DataValid;
            }
        };


        public class EpsilonDisplay
        {
            public EpsilonCommandsIndex ReadCommand;
            public EpsilonCommandsIndex WriteCommand;

            public DisplayMode GetDisplayMode;

            public enum DisplayMode:byte
            {
                TOD_Format_1 = 0,
                TOD_Format_2 = 1,
                TOD_Format_3 = 2,
                TOD_Format_4 = 3,
                TOD_Format_5 = 4,
            }
            public bool DataValid;

            public EpsilonDisplay()
            {
                ReadCommand = EpsilonCommandsIndex.Display_Read;
                WriteCommand = EpsilonCommandsIndex.Display_Write;
            }

            public bool Serialize(out List<byte> payload)
            {
                bool retval = true;

                payload = new List<byte>(2);

                payload.Add((byte)GetDisplayMode);

                payload.Add(0);

                return retval;
            }

            public bool ProcessMessage(EpsilonSerialMessage currentmessage)
            {

                if (currentmessage.MessageID != (byte)ReadCommand)
                {
                    return false;
                }
                DataValid = true;


                switch (currentmessage.Payload[0])
                {
                    case 0:
                        GetDisplayMode = DisplayMode.TOD_Format_1;
                        break;
                    case 1:
                        GetDisplayMode = DisplayMode.TOD_Format_2;
                        break;
                    case 2:
                        GetDisplayMode = DisplayMode.TOD_Format_3;
                        break;
                    case 3:
                        GetDisplayMode = DisplayMode.TOD_Format_4;
                        break;
                    case 4:
                        GetDisplayMode = DisplayMode.TOD_Format_5;
                        break;
                    default:
                        return false;
                }

                DataValid = true;

                return DataValid;
            }
        };


        public class EpsilonTODMessage
        {
            //public EpsilonCommandInfo ReadCommand;
            //EpsilonCommandInfo WriteCommand;
            public string GetCurrentTODString;
            public string GetCurrentFriendlyString;

            //public byte DisplayMode;
            public bool DataValid;
            public TODFormat CurrentDisplayFormat;

            

            public enum TODFormat : byte
            {
                TOD_Format_1 = 193,
                TOD_Format_2 = 194,
                TOD_Format_3 = 195,
                TOD_Format_4 = 196,
                TOD_Format_5 = 197,
            }

            public bool ProcessMessage(EpsilonSerialMessage currentmessage, ref Dictionary<int, EpsilonCommandInfo> info)
            {
                DataValid = true;
                DateTime receivedtime = new DateTime();
                int year;
                int dayofyear;
                int sourceoffset = 7;
                string timeformat = "yyyy-MM-ddTHH:mm:ss";
                string timesuffix = "";

                CurrentDisplayFormat = (TODFormat)currentmessage.MessageID;

                EpsilonCommandInfo e;
                info.TryGetValue(currentmessage.MessageID, out e);

                // leap second is relevant to all modes except 4, which is just a double
                if (CurrentDisplayFormat != TODFormat.TOD_Format_4)
                {
                    // preemptive leap second handling
                    if (currentmessage.Payload[6] == 60)
                    {
                        currentmessage.Payload[6] = 59;
                    }
                }

                if (CurrentDisplayFormat == TODFormat.TOD_Format_1 || CurrentDisplayFormat == TODFormat.TOD_Format_2)
                {
                    year = currentmessage.Payload[2] << 8 | currentmessage.Payload[3];


                    switch (CurrentDisplayFormat)
                    {
                        case TODFormat.TOD_Format_1:
                            receivedtime = new DateTime(year,
                                currentmessage.Payload[1],
                                currentmessage.Payload[0],
                                currentmessage.Payload[4],
                                currentmessage.Payload[5],
                                currentmessage.Payload[6]);
                            break;
                        case TODFormat.TOD_Format_2:
                            receivedtime = new DateTime(year,
                                currentmessage.Payload[0],
                                currentmessage.Payload[1],
                                currentmessage.Payload[4],
                                currentmessage.Payload[5],
                                currentmessage.Payload[6]);
                            break;
                    }

                }
                if (CurrentDisplayFormat == TODFormat.TOD_Format_4)
                {
                    sourceoffset = 8;
                }


                string timesource = "";
                switch (Convert.ToChar(currentmessage.Payload[sourceoffset]))
                {
                    case 'G':
                        timesource = "GPS";
                        timesuffix = "G";
                        break;
                    case 'N':
                        timesource = "Not Locked";
                        timesuffix = "N";
                        break;
                    case 'L':
                        timesource = "Local Time";
                        timesuffix = "L";
                        break;
                    case 'M':
                        timesource = "Manually Set";
                        timesuffix = "M";
                        break;
                    case 'U':
                        timesource = "UTC";
                        timesuffix = "Z";
                        break;
                    default:
                        timesource = "N/A";
                        break;

                }

                if (CurrentDisplayFormat == TODFormat.TOD_Format_3)
                {
                    year = currentmessage.Payload[2] << 8 | currentmessage.Payload[3];
                    dayofyear = currentmessage.Payload[0] << 8 | currentmessage.Payload[1];
                    receivedtime = new DateTime(year, 1, 1,
                        currentmessage.Payload[4],
                        currentmessage.Payload[5],
                        currentmessage.Payload[6]);
                    timeformat = "HH:mm:ss";

                    GetCurrentTODString =
                        String.Format("{2}/{1} - {0}",
                        receivedtime.ToString(timeformat).Replace('.', ':'),
                        year.ToString(),
                        dayofyear.ToString());
                }
                else if (CurrentDisplayFormat == TODFormat.TOD_Format_4)
                {
                    // MJD format is double with 6 decimal places
                    List<byte> doubledata = currentmessage.Payload;
                    doubledata.RemoveAt(8);

                    doubledata.Reverse();
                    //byte[] doubledata_bytes = doubledata.ToArray();

                    double MJD_Time = BitConverter.ToDouble(doubledata.ToArray(), 0);
                    /*double MJD_Time =
                        (Int64)(currentmessage.Payload[0] << 56) |
                        (Int64)(currentmessage.Payload[1] << 48) |
                        (Int64)(currentmessage.Payload[2] << 40) |
                        (Int64)(currentmessage.Payload[3] << 32) |
                        (Int64)(currentmessage.Payload[4] << 24) |
                        (Int64)(currentmessage.Payload[5] << 16) |
                        (Int64)(currentmessage.Payload[6] << 8) |
                        (Int64)(currentmessage.Payload[7] & 0xff);*/
                    GetCurrentTODString =
                        String.Format("{0:N6}",
                        MJD_Time);
                }
                else if (CurrentDisplayFormat == TODFormat.TOD_Format_5)
                {
                    Int32 MJD_Int = (Int32)currentmessage.Payload[0] << 24 | (Int32)currentmessage.Payload[1] << 16 |
                    (Int32)currentmessage.Payload[2] << 8 | (Int32)currentmessage.Payload[3];
                    // set TOD using last part
                    receivedtime = new DateTime(1, 1, 1,
                                currentmessage.Payload[4],
                                currentmessage.Payload[5],
                                currentmessage.Payload[6]);
                    timeformat = "HH:mm:ss";

                    GetCurrentTODString =
                        String.Format("{1} / {0}",
                        receivedtime.ToString(timeformat).Replace('.', ':'),
                        MJD_Int.ToString());

                }
                else
                {
                    GetCurrentTODString = String.Format("{0}", receivedtime.ToString(timeformat).Replace('.', ':'), timesource);

                }

                GetCurrentTODString += timesuffix;

                GetCurrentFriendlyString = String.Format("Source is {0}, {1}", timesource, e.FriendlyName);


                return DataValid;
            }
        }



        public class EpsilonAlarmInformation
        {
            public EpsilonCommandsIndex ReadCommand;
            public EpsilonCommandsIndex WriteCommand;

            public EpsilonAlarmAccuracy AlarmAccuracy;
            public decimal FrequencyAlarmLimit;
            public UInt32 PhaseAlarmLimit;
            public bool FrequencyOutputSquelch;
            public bool DataValid;

            public enum EpsilonAlarmAccuracy
            {
                AlarmResolution1ppb,
                AlarmResolution0_1ppb
            };

            public bool Serialize(out List<byte> payload)
            {
                bool retval = true;

                payload = new List<byte>(10);

                byte[] phasealarm = BitConverter.GetBytes(PhaseAlarmLimit);
                if (BitConverter.IsLittleEndian)
                    Array.Reverse(phasealarm);

                payload.AddRange(phasealarm);

                byte[] frequency;
                if (AlarmAccuracy == EpsilonAlarmAccuracy.AlarmResolution0_1ppb)
                {
                     frequency = BitConverter.GetBytes((UInt32)(FrequencyAlarmLimit * 10));
                    frequency[3] |= 0b10000000;
                }
                else
                {
                    frequency = BitConverter.GetBytes((UInt32)(FrequencyAlarmLimit));
                }

                if (BitConverter.IsLittleEndian)
                    Array.Reverse(frequency);
                

                payload.AddRange(frequency);

                payload.Add(FrequencyOutputSquelch ? (byte)0 : (byte)1);

                payload.Add(0);

                return retval;
            }

            public bool ProcessMessage(EpsilonSerialMessage currentmessage)
            {
                if (currentmessage.MessageID != (byte)ReadCommand)
                {
                    return false;
                }
                DataValid = true;

                PhaseAlarmLimit = (UInt32)currentmessage.Payload[0] << 24 | (UInt32)currentmessage.Payload[1] << 16 |
                                (UInt32)currentmessage.Payload[2] << 8 | (UInt32)currentmessage.Payload[3];

                // if this bit is set on RX, we can use fine steps
                bool support_fine_step_frequency = (currentmessage.Payload[4] & 0b10000000) > 0;

                AlarmAccuracy = support_fine_step_frequency ? 
                    EpsilonAlarmAccuracy.AlarmResolution0_1ppb : EpsilonAlarmAccuracy.AlarmResolution1ppb;

                currentmessage.Payload[4] &= 0b01111111; // clear bit if it was high

                UInt32 currentlong = (UInt32)currentmessage.Payload[4] << 24 | (UInt32)currentmessage.Payload[5] << 16 |
                    (UInt32)currentmessage.Payload[6] << 8 | (UInt32)currentmessage.Payload[7];


                if (support_fine_step_frequency)
                { 
                    FrequencyAlarmLimit = (decimal)currentlong / 10;
                }
                else
                {
                    FrequencyAlarmLimit = (decimal)currentlong;
                }

                FrequencyOutputSquelch = currentmessage.Payload[8] == 0;

                return DataValid;
            }
        }



        public class EpsilonManualSecondCorrection
        {
            public EpsilonCommandsIndex ReadCommand;
            public EpsilonCommandsIndex WriteCommand;
            public EpsilonManualSecondCorrectionTypes CorrectionType;

            public enum EpsilonManualSecondCorrectionTypes : byte
            {
                Add_1_Second = 0,
                Subtract_1_Second = 1,
                //Undefined = 2
            };

            public bool DataValid;

            public bool Serialize(out List<byte> payload)
            {
                payload = new List<byte>(1);

                payload.Add(CorrectionType == EpsilonManualSecondCorrectionTypes.Subtract_1_Second ?
                    (byte)1 : (byte)0);

                return true;
            }

            public bool ProcessMessage(EpsilonSerialMessage currentmessage)
            {
                if (currentmessage.MessageID != (byte)ReadCommand)
                {
                    return false;
                }
                DataValid = true;

                CorrectionType = currentmessage.Payload[0] == 0 ? 
                    EpsilonManualSecondCorrectionTypes.Add_1_Second : EpsilonManualSecondCorrectionTypes.Subtract_1_Second;

                return DataValid;
            }
        }

        public class EpsilonManualTOD
        {
            public EpsilonCommandsIndex ReadCommand;
            public EpsilonCommandsIndex WriteCommand;
            public DateTime ManualTime
            {
                get
                {
                    return _manualtime;
                }
                set
                {
                    if (DateTime.Compare(value, minimumtime) < 0)
                    {
                        throw new ArgumentOutOfRangeException();
                    }
                    if (DateTime.Compare(value, maximumtime) > 0)
                    {
                        throw new ArgumentOutOfRangeException();
                    }
                    _manualtime = value;
                }

            }

            public DateTime minimumtime = new DateTime(1992, 1, 1, 0, 0, 0);
            public DateTime maximumtime = new DateTime(2127, 12, 31, 23, 59, 59);
            private DateTime _manualtime;

            public bool DataValid;

            public bool Serialize(out List<byte> payload)
            {
                payload = new List<byte>(7);

                DateTime time = ManualTime;

                payload.Add((byte)time.Day);
                payload.Add((byte)time.Month);

                payload.Add((byte)(time.Year >> 8));
                payload.Add((byte)(time.Year & 0xff));

                payload.Add((byte)time.Hour);
                payload.Add((byte)time.Minute);
                payload.Add((byte)time.Second);

                return true;
            }

            public bool ProcessMessage(EpsilonSerialMessage currentmessage)
            {
                if (currentmessage.MessageID != (byte)ReadCommand)
                {
                    return false;
                }
                //DataValid = true;
                try
                {
                    DateTime rx = new DateTime((int)(currentmessage.Payload[2] << 8 | currentmessage.Payload[3]),
                                    (int)currentmessage.Payload[1],
                                    (int)currentmessage.Payload[0],
                                    (int)currentmessage.Payload[4],
                                    (int)currentmessage.Payload[5],
                                    (int)currentmessage.Payload[6]);
                    ManualTime = rx;
                    return true;
                }
                catch (Exception e)
                {
                    return false;
                }
            }

        }

        public class EpsilonTODSetup
        {
            public EpsilonCommandsIndex ReadCommand;
            public EpsilonCommandsIndex WriteCommand;

            public EpsilonTimeOfDayTypes TODSetup;
            public bool DataValid;

            public enum EpsilonTimeOfDayTypes : byte
            {
                TOD_Message_Output = 0,
                TOD_Diagnostic_Output = 1
            };

            public bool Serialize(out List<byte> payload)
            {
                payload = new List<byte>(1);

                payload.Add((byte)TODSetup);

                return true;
            }

            public bool ProcessMessage(EpsilonSerialMessage currentmessage)
            {
                if (currentmessage.MessageID != (byte)ReadCommand)
                {
                    return false;
                }
                DataValid = true;

                TODSetup = currentmessage.Payload[0] == 0 ? EpsilonTimeOfDayTypes.TOD_Message_Output : EpsilonTimeOfDayTypes.TOD_Diagnostic_Output;

                return DataValid;
            }
        };

        public class EpsilonTODOutputPeriod
        {
            public EpsilonCommandsIndex ReadCommand;
            public EpsilonCommandsIndex WriteCommand;
            public UInt32 TOD_Output_Period;

            public bool DataValid;

            public bool Serialize(out List<byte> payload)
            {
                payload = new List<byte>(4);

                byte[] todperiod = BitConverter.GetBytes(TOD_Output_Period);
                if (BitConverter.IsLittleEndian)
                    Array.Reverse(todperiod);

                payload.AddRange(todperiod);

                return true;
            }

            public bool ProcessMessage(EpsilonSerialMessage currentmessage)
            {
                if (currentmessage.MessageID != (byte)ReadCommand)
                {
                    return false;
                }
                DataValid = true;

                TOD_Output_Period = (UInt32)currentmessage.Payload[0] << 24 | (UInt32)currentmessage.Payload[1] << 16 |
                                (UInt32)currentmessage.Payload[2] << 8 | (UInt32)currentmessage.Payload[3];

                return DataValid;
            }

        }

        public class EpsilonManual1PPSPhaseCorrection
        {
            public EpsilonCommandsIndex ReadCommand;
            public EpsilonCommandsIndex WriteCommand;
            public bool DataValid;
            public decimal PPSCorrectionValue
            {
                get
                {
                    return (decimal)_ppscorrectionvalue / 10;
                }
                set
                {
                    _ppscorrectionvalue = (Int32)(value * 10);

                    if (_ppscorrectionvalue > _ppscorrectionvalue_highlimit)
                    {
                        _ppscorrectionvalue = _ppscorrectionvalue_highlimit;
                    }
                    else if (_ppscorrectionvalue < _ppscorrectionvalue_lowlimit)
                    {
                        _ppscorrectionvalue = _ppscorrectionvalue_lowlimit;
                    }
                }
            }
            private Int32 _ppscorrectionvalue_highlimit = 50000;
            private Int32 _ppscorrectionvalue_lowlimit = -50000;
            private Int32 _ppscorrectionvalue;

            public bool Serialize(out List<byte> payload)
            {
                payload = new List<byte>(4);

                Int32 val = _ppscorrectionvalue;

                payload.Add((byte)((Int32)val >> 24));
                payload.Add((byte)((Int32)val >> 16));
                payload.Add((byte)((Int32)val >> 8));
                payload.Add((byte)((Int32)val & 0xff));


                return true;
            }

            public bool ProcessMessage(EpsilonSerialMessage currentmessage)
            {
                if (currentmessage.MessageID != (byte)ReadCommand)
                {
                    return false;
                }
                DataValid = true;

                PPSCorrectionValue = (Int32)currentmessage.Payload[0] << 24 | (Int32)currentmessage.Payload[1] << 16 |
                                (Int32)currentmessage.Payload[2] << 8 | (Int32)currentmessage.Payload[3];

                return DataValid;
            }

        }

        public class EpsilonPhaseCorrection
        {
            public EpsilonCommandsIndex ReadCommand;
            public EpsilonCommandsIndex WriteCommand;
            public bool DataValid;
            public UInt32 Phase_Correction_Value;

            public bool Serialize(out List<byte> payload)
            {
                payload = new List<byte>(4);

                byte[] phasealarm = BitConverter.GetBytes(Phase_Correction_Value);
                if (BitConverter.IsLittleEndian)
                    Array.Reverse(phasealarm);

                payload.AddRange(phasealarm);

                return true;
            }

            public bool ProcessMessage(EpsilonSerialMessage currentmessage)
            {
                if (currentmessage.MessageID != (byte)ReadCommand)
                {
                    return false;
                }
                DataValid = true;

                Phase_Correction_Value = (UInt32)currentmessage.Payload[0] << 24 | (UInt32)currentmessage.Payload[1] << 16 |
                                (UInt32)currentmessage.Payload[2] << 8 | (UInt32)currentmessage.Payload[3];

                return DataValid;
            }

        }

        public class EpsilonForcedHoldover
        {
            public EpsilonCommandsIndex ReadCommand;
            public EpsilonCommandsIndex WriteCommand;
            public bool Forced_Holdover;
            public bool DataValid;

            public bool Serialize(out List<byte> payload)
            {
                payload = new List<byte>(1);

                payload.Add(Forced_Holdover ?
                    (byte)0 : (byte)1);

                return true;
            }

            public bool ProcessMessage(EpsilonSerialMessage currentmessage)
            {
                if (currentmessage.MessageID != (byte)ReadCommand)
                {
                    return false;
                }
                DataValid = true;

                Forced_Holdover = currentmessage.Payload[0] == 1 ? false : true;

                return DataValid;
            }
        }

        public class EpsilonRemoteControl
        {
            public EpsilonCommandsIndex ReadCommand;
            public EpsilonCommandsIndex WriteCommand;
            public bool RemoteAllowed;
            public bool DataValid;

            public bool Serialize(out List<byte> payload)
            {
                payload = new List<byte>(1);

                payload.Add(RemoteAllowed ?
                    (byte)0 : (byte)1);

                return true;
            }

            public bool ProcessMessage(EpsilonSerialMessage currentmessage)
            {
                if (currentmessage.MessageID != (byte)ReadCommand)
                {
                    return false;
                }
                DataValid = true;

                RemoteAllowed = currentmessage.Payload[0] == 1 ? false : true;

                return DataValid;
            }
        }

        // class representing all information about a device
        class EpsilonDeviceContext
        {
            public EpsilonStatusMessageResponse LastStatusMessage;
            public EpsilonVersionMessage LastVersionMessage;
            public EpsilonTODOutputPeriod TOD_Output_Period;
            public EpsilonTODSetup TOD_Setup;
            public EpsilonGPSInformation GPS_Info;
            public EpsilonLocalTimeOffset Local_Time_Offset;
            public EpsilonPhaseCorrection Phase_Correction_Value;
            public EpsilonLeapStatus Leap_Second_Information;
            public EpsilonTODMessage TOD_Information;
            public EpsilonForcedHoldover Forced_Holdover;
            public EpsilonManualTOD ManualTimeSetting;
            public EpsilonManualSecondCorrection ManualSecondCorrection;
            public EpsilonRemoteControl RemoteControlAuthorized;
            public EpsilonAlarmInformation AlarmInfo;
            public EpsilonDisplay DisplayInfo;
            public EpsilonManual1PPSPhaseCorrection Manual1PPS;
            public EpsilonManualTOD GPSTimeInit;

            public EpsilonDeviceContext()
            {
                Epsilon_Command_List = new Dictionary<int, EpsilonCommandInfo>();
                

                LastStatusMessage = new EpsilonStatusMessageResponse();
                LastVersionMessage = new EpsilonVersionMessage();
                TOD_Output_Period = new EpsilonTODOutputPeriod();
                GPS_Info = new EpsilonGPSInformation();
                TOD_Setup = new EpsilonTODSetup();
                Local_Time_Offset = new EpsilonLocalTimeOffset();
                Phase_Correction_Value = new EpsilonPhaseCorrection();
                Leap_Second_Information = new EpsilonLeapStatus();
                TOD_Information = new EpsilonTODMessage();
                Forced_Holdover = new EpsilonForcedHoldover();
                ManualTimeSetting = new EpsilonManualTOD();
                ManualSecondCorrection = new EpsilonManualSecondCorrection();
                AlarmInfo = new EpsilonAlarmInformation();
                Manual1PPS = new EpsilonManual1PPSPhaseCorrection();
                DisplayInfo = new EpsilonDisplay();
                RemoteControlAuthorized = new EpsilonRemoteControl();
                GPSTimeInit = new EpsilonManualTOD();

                GPSTimeInit.ReadCommand = EpsilonCommandsIndex.GPS_Init_Time_Read;

                Populate_Epsilon_Command_List();

            }

            public Dictionary<int, EpsilonCommandInfo> Epsilon_Command_List;

            public bool ProcessRXCommand(EpsilonSerialMessage currentmessage)
            {
                // check if we know this message
                EpsilonCommandInfo e;
                if (!Epsilon_Command_List.TryGetValue(currentmessage.MessageID, out e))
                {
                    return false;
                }

                if (currentmessage.Payload.Count != e.Payload_Size)
                {
                    // message looks like garbage data
                    return false;
                }

                bool retval = false;

                switch (e.Command_Index)
                {
                    case EpsilonCommandsIndex.Alarm_Limits_Read:
                        retval = AlarmInfo.ProcessMessage(currentmessage);
                        break;
                    case EpsilonCommandsIndex.Display_Read:
                        retval = DisplayInfo.ProcessMessage(currentmessage);
                        break;
                    case EpsilonCommandsIndex.Force_Holdover_Read:
                        retval = Forced_Holdover.ProcessMessage(currentmessage);
                        break;
                    case EpsilonCommandsIndex.GPS_Init_Read:
                        retval = GPS_Info.ProcessMessage(currentmessage);
                        break;
                    case EpsilonCommandsIndex.Leap_Second_Read:
                        retval = Leap_Second_Information.ProcessMessage(currentmessage);
                        break;
                    case EpsilonCommandsIndex.Local_Time_Read:
                        retval = Local_Time_Offset.ProcessMessage(currentmessage);
                        break;
                    case EpsilonCommandsIndex.Manual_1PPS_Read:
                        retval = Manual1PPS.ProcessMessage(currentmessage);
                        break;
                    case EpsilonCommandsIndex.Manual_Second_Read:
                        retval = ManualSecondCorrection.ProcessMessage(currentmessage);
                        break;
                    case EpsilonCommandsIndex.Manual_Time_Read:
                        retval = ManualTimeSetting.ProcessMessage(currentmessage);
                        break;
                    case EpsilonCommandsIndex.Phase_Correction_Read:
                        retval = Phase_Correction_Value.ProcessMessage(currentmessage);
                        break;
                    case EpsilonCommandsIndex.Remote_Control_Read:
                        retval = RemoteControlAuthorized.ProcessMessage(currentmessage);
                        break;
                    case EpsilonCommandsIndex.Status_Read:
                        retval = LastStatusMessage.ProcessMessage(currentmessage);
                        break;
                    case EpsilonCommandsIndex.TOD_Format_1:
                    case EpsilonCommandsIndex.TOD_Format_2:
                    case EpsilonCommandsIndex.TOD_Format_3:
                    case EpsilonCommandsIndex.TOD_Format_4:
                    case EpsilonCommandsIndex.TOD_Format_5:
                        retval = TOD_Information.ProcessMessage(currentmessage, ref Epsilon_Command_List);
                        break;
                    case EpsilonCommandsIndex.TOD_Period_Read:
                        retval = TOD_Output_Period.ProcessMessage(currentmessage);
                        break;
                    case EpsilonCommandsIndex.TOD_Setup_Read:
                        retval = TOD_Setup.ProcessMessage(currentmessage);
                        break;
                    case EpsilonCommandsIndex.Version_Read:
                        retval = LastVersionMessage.ProcessMessage(currentmessage);
                        break;
                    case EpsilonCommandsIndex.GPS_Init_Time_Read:
                        retval = GPSTimeInit.ProcessMessage(currentmessage);
                        break;
                }

                return retval;

            }

            private void Populate_Epsilon_Command_List()
            {
                // populate dictionary with all valid commands
                EpsilonCommandInfo e = new EpsilonCommandInfo();

                e.Command_Response_Type = EpsilonCommandResponseType.Query_Response;

                e.Command_Index = EpsilonCommandsIndex.TOD_Format_1;
                e.Payload_Size = 8;
                e.ReadWrite = EpsilonCommandAccessType.Read;
                e.FriendlyName = "TOD Format 1";
                e.Write_Conditions = EpsilonCommandWriteConditions.Always_Allowed;
                Epsilon_Command_List.Add((int)e.Command_Index, e);
                

                e = new EpsilonCommandInfo();
                e.Command_Index = EpsilonCommandsIndex.TOD_Format_2;
                e.Payload_Size = 8;
                e.ReadWrite = EpsilonCommandAccessType.Read;
                e.FriendlyName = "TOD Format 2";
                e.Write_Conditions = EpsilonCommandWriteConditions.Always_Allowed;
                Epsilon_Command_List.Add((int)e.Command_Index, e);


                e = new EpsilonCommandInfo();
                e.Command_Index = EpsilonCommandsIndex.TOD_Format_3;
                e.Payload_Size = 8;
                e.ReadWrite = EpsilonCommandAccessType.Read;
                e.FriendlyName = "TOD Format 3 (DoY/Y)";
                e.Write_Conditions = EpsilonCommandWriteConditions.Always_Allowed;
                Epsilon_Command_List.Add((int)e.Command_Index, e);

                e = new EpsilonCommandInfo();
                e.Command_Index = EpsilonCommandsIndex.TOD_Format_4;
                e.Payload_Size = 8;
                e.ReadWrite = EpsilonCommandAccessType.Read;
                e.FriendlyName = "TOD Format 4 (MJD Double)";
                e.Write_Conditions = EpsilonCommandWriteConditions.Always_Allowed;
                Epsilon_Command_List.Add((int)e.Command_Index, e);

                e = new EpsilonCommandInfo();
                e.Command_Index = EpsilonCommandsIndex.TOD_Format_5;
                e.Payload_Size = 8;
                e.ReadWrite = EpsilonCommandAccessType.Read;
                e.FriendlyName = "TOD Format 5 (MJD Integer)";
                e.Write_Conditions = EpsilonCommandWriteConditions.Always_Allowed;
                Epsilon_Command_List.Add((int)e.Command_Index, e);

                e = new EpsilonCommandInfo();
                e.Command_Index = EpsilonCommandsIndex.Status_Read;
                e.Payload_Size = 37;
                e.ReadWrite = EpsilonCommandAccessType.Read;
                e.FriendlyName = "Status";
                e.Write_Conditions = EpsilonCommandWriteConditions.Always_Allowed;
                Epsilon_Command_List.Add((int)e.Command_Index, e);

                LastStatusMessage.ReadCommand = e.Command_Index;


                e = new EpsilonCommandInfo();
                e.Command_Index = EpsilonCommandsIndex.TOD_Setup_Read;
                e.Payload_Size = 1;
                e.FriendlyName = "Time of Day Configuration";
                e.ReadWrite = EpsilonCommandAccessType.Read;
                e.Write_Conditions = EpsilonCommandWriteConditions.Always_Allowed;
                Epsilon_Command_List.Add((int)e.Command_Index, e);

                TOD_Setup.ReadCommand = e.Command_Index;

                e = new EpsilonCommandInfo();
                e.Command_Index = EpsilonCommandsIndex.TOD_Period_Read;
                e.Payload_Size = 4;
                e.FriendlyName = "Time of Day Interval";
                e.ReadWrite = EpsilonCommandAccessType.Read;
                e.Write_Conditions = EpsilonCommandWriteConditions.Always_Allowed;
                Epsilon_Command_List.Add((int)e.Command_Index, e);
                TOD_Output_Period.ReadCommand = e.Command_Index;

                e = new EpsilonCommandInfo();
                e.Command_Index = EpsilonCommandsIndex.GPS_Init_Read;
                e.Payload_Size = 19;
                e.FriendlyName = "GPS Parameters";
                e.ReadWrite = EpsilonCommandAccessType.Read;
                e.Write_Conditions = EpsilonCommandWriteConditions.Always_Allowed;
                Epsilon_Command_List.Add((int)e.Command_Index, e);
                GPS_Info.ReadCommand = e.Command_Index;

                e = new EpsilonCommandInfo();
                e.Command_Index = EpsilonCommandsIndex.Local_Time_Read;
                e.Payload_Size = 2;
                e.FriendlyName = "Local Time Offset";
                e.ReadWrite = EpsilonCommandAccessType.Read;
                e.Write_Conditions = EpsilonCommandWriteConditions.Always_Allowed;
                Epsilon_Command_List.Add((int)e.Command_Index, e);
                Local_Time_Offset.ReadCommand = e.Command_Index;

                e = new EpsilonCommandInfo();
                e.Command_Index = EpsilonCommandsIndex.Display_Read;
                e.Payload_Size = 2;
                e.FriendlyName = "Display/TOD Format";
                e.ReadWrite = EpsilonCommandAccessType.Read;
                e.Write_Conditions = EpsilonCommandWriteConditions.Always_Allowed;
                Epsilon_Command_List.Add((int)e.Command_Index, e);
                DisplayInfo.ReadCommand = e.Command_Index;

                e = new EpsilonCommandInfo();
                e.Command_Index = EpsilonCommandsIndex.Alarm_Limits_Read;
                e.Payload_Size = 10;
                e.FriendlyName = "Alarm Limits";
                e.ReadWrite = EpsilonCommandAccessType.Read;
                e.Write_Conditions = EpsilonCommandWriteConditions.Always_Allowed;
                Epsilon_Command_List.Add((int)e.Command_Index, e);
                AlarmInfo.ReadCommand = e.Command_Index;

                e = new EpsilonCommandInfo();
                e.Command_Index = EpsilonCommandsIndex.Version_Read;
                e.Payload_Size = 10;
                e.FriendlyName = "Version/Hardware Info";
                e.ReadWrite = EpsilonCommandAccessType.Read;
                e.Write_Conditions = EpsilonCommandWriteConditions.Always_Allowed;
                Epsilon_Command_List.Add((int)e.Command_Index, e);
                LastVersionMessage.ReadCommand = e.Command_Index;

                e = new EpsilonCommandInfo();
                e.Command_Index = EpsilonCommandsIndex.Force_Holdover_Read;
                e.Payload_Size = 1;
                e.FriendlyName = "Forced Holdover Status";
                e.ReadWrite = EpsilonCommandAccessType.Read;
                e.Write_Conditions = EpsilonCommandWriteConditions.Always_Allowed;
                Epsilon_Command_List.Add((int)e.Command_Index, e);
                Forced_Holdover.ReadCommand = e.Command_Index;

                e = new EpsilonCommandInfo();
                e.Command_Index = EpsilonCommandsIndex.Manual_Time_Read;
                e.Payload_Size = 7;
                e.FriendlyName = "Manual Time Set";
                e.ReadWrite = EpsilonCommandAccessType.Read;
                e.Write_Conditions = EpsilonCommandWriteConditions.Always_Allowed;
                Epsilon_Command_List.Add((int)e.Command_Index, e);
                ManualTimeSetting.ReadCommand = e.Command_Index;

                e = new EpsilonCommandInfo();
                e.Command_Index = EpsilonCommandsIndex.Manual_Second_Read;
                e.Payload_Size = 1;
                e.FriendlyName = "Manual Time Second Offset";
                e.ReadWrite = EpsilonCommandAccessType.Read;
                e.Write_Conditions = EpsilonCommandWriteConditions.Always_Allowed;
                Epsilon_Command_List.Add((int)e.Command_Index, e);
                ManualSecondCorrection.ReadCommand = e.Command_Index;

                e = new EpsilonCommandInfo();
                e.Command_Index = EpsilonCommandsIndex.Manual_1PPS_Read;
                e.Payload_Size = 4;
                e.FriendlyName = "Manual Time 1PPS Phase Adjust";
                e.ReadWrite = EpsilonCommandAccessType.Read;
                e.Write_Conditions = EpsilonCommandWriteConditions.Always_Allowed;
                Epsilon_Command_List.Add((int)e.Command_Index, e);
                Manual1PPS.ReadCommand = e.Command_Index;

                e = new EpsilonCommandInfo();
                e.Command_Index = EpsilonCommandsIndex.Remote_Control_Read;
                e.Payload_Size = 1;
                e.FriendlyName = "Remote Control Enable";
                e.ReadWrite = EpsilonCommandAccessType.Read;
                e.Write_Conditions = EpsilonCommandWriteConditions.Always_Allowed;
                Epsilon_Command_List.Add((int)e.Command_Index, e);
                RemoteControlAuthorized.ReadCommand = e.Command_Index;

                e = new EpsilonCommandInfo();
                e.Command_Index = EpsilonCommandsIndex.Phase_Correction_Read;
                e.Payload_Size = 4;
                e.FriendlyName = "Antenna Phase Correction";
                e.ReadWrite = EpsilonCommandAccessType.Read;
                e.Write_Conditions = EpsilonCommandWriteConditions.Always_Allowed;
                Epsilon_Command_List.Add((int)e.Command_Index, e);
                Phase_Correction_Value.ReadCommand = e.Command_Index;

                e = new EpsilonCommandInfo();
                e.Command_Index = EpsilonCommandsIndex.Leap_Second_Read;
                e.Payload_Size = 6;
                e.FriendlyName = "Leap Second Information";
                e.ReadWrite = EpsilonCommandAccessType.Read;
                e.Write_Conditions = EpsilonCommandWriteConditions.Always_Allowed;
                Epsilon_Command_List.Add((int)e.Command_Index, e);
                Leap_Second_Information.ReadCommand = e.Command_Index;

                e = new EpsilonCommandInfo();
                e.Command_Response_Type = EpsilonCommandResponseType.Command_ACK;
                // write commands
                e.Command_Index = EpsilonCommandsIndex.TOD_Setup_Write;
                e.Payload_Size = 1;
                e.FriendlyName = "Time of Day Configuration";
                e.ReadWrite = EpsilonCommandAccessType.Write;
                e.Write_Conditions = EpsilonCommandWriteConditions.Remote_Must_Be_Set;
                Epsilon_Command_List.Add((int)e.Command_Index, e);


                e = new EpsilonCommandInfo();
                e.Command_Index = EpsilonCommandsIndex.TOD_Period_Write;
                e.Payload_Size = 4;
                e.FriendlyName = "Time of Day Interval";
                e.ReadWrite = EpsilonCommandAccessType.Write;
                e.Write_Conditions = EpsilonCommandWriteConditions.Remote_Must_Be_Set;
                Epsilon_Command_List.Add((int)e.Command_Index, e);

                e = new EpsilonCommandInfo();
                e.Command_Index = EpsilonCommandsIndex.GPS_Init_Write;
                e.Payload_Size = 19;
                e.FriendlyName = "GPS Parameters";
                e.ReadWrite = EpsilonCommandAccessType.Write;
                e.Write_Conditions = EpsilonCommandWriteConditions.Remote_No_Holdover;
                Epsilon_Command_List.Add((int)e.Command_Index, e);

                e = new EpsilonCommandInfo();
                e.Command_Index = EpsilonCommandsIndex.Local_Time_Write;
                e.Payload_Size = 2;
                e.FriendlyName = "Local Time Offset";
                e.ReadWrite = EpsilonCommandAccessType.Write;
                e.Write_Conditions = EpsilonCommandWriteConditions.Remote_No_Holdover;
                Epsilon_Command_List.Add((int)e.Command_Index, e);

                e = new EpsilonCommandInfo();
                e.Command_Index = EpsilonCommandsIndex.Display_Write;
                e.Payload_Size = 2;
                e.FriendlyName = "Display/TOD Format";
                e.ReadWrite = EpsilonCommandAccessType.Write;
                e.Write_Conditions = EpsilonCommandWriteConditions.Remote_Must_Be_Set;
                Epsilon_Command_List.Add((int)e.Command_Index, e);

                e = new EpsilonCommandInfo();
                e.Command_Index = EpsilonCommandsIndex.Alarm_Limit_Write;
                e.Payload_Size = 10;
                e.FriendlyName = "Alarm Limits";
                e.ReadWrite = EpsilonCommandAccessType.Write;
                e.Write_Conditions = EpsilonCommandWriteConditions.Remote_Must_Be_Set;
                Epsilon_Command_List.Add((int)e.Command_Index, e);

                e = new EpsilonCommandInfo();
                e.Command_Index = EpsilonCommandsIndex.Version_Write;
                e.Payload_Size = 10;
                e.FriendlyName = "Version/Hardware Info";
                e.ReadWrite = EpsilonCommandAccessType.Write;
                e.Write_Conditions = EpsilonCommandWriteConditions.Remote_Must_Be_Set;
                Epsilon_Command_List.Add((int)e.Command_Index, e);
                //LastVersionMessage.ReadCommand = e.Command_Index;

                e = new EpsilonCommandInfo();
                e.Command_Index = EpsilonCommandsIndex.Force_Holdover_Write;
                e.Payload_Size = 1;
                e.FriendlyName = "Forced Holdover Status";
                e.ReadWrite = EpsilonCommandAccessType.Write;
                e.Write_Conditions = EpsilonCommandWriteConditions.Remote_Must_Be_Set;
                Epsilon_Command_List.Add((int)e.Command_Index, e);

                e = new EpsilonCommandInfo();
                e.Command_Index = EpsilonCommandsIndex.Manual_Time_Write;
                e.Payload_Size = 7;
                e.FriendlyName = "Manual Time Set";
                e.ReadWrite = EpsilonCommandAccessType.Write;
                e.Write_Conditions = EpsilonCommandWriteConditions.Remote_Only_Holdover;
                Epsilon_Command_List.Add((int)e.Command_Index, e);

                e = new EpsilonCommandInfo();
                e.Command_Index = EpsilonCommandsIndex.Manual_Second_Write;
                e.Payload_Size = 1;
                e.FriendlyName = "Manual Time Second Offset";
                e.ReadWrite = EpsilonCommandAccessType.Write;
                e.Write_Conditions = EpsilonCommandWriteConditions.Remote_Only_Holdover;
                Epsilon_Command_List.Add((int)e.Command_Index, e);

                e = new EpsilonCommandInfo();
                e.Command_Index = EpsilonCommandsIndex.Manual_1PPS_Write;
                e.Payload_Size = 4;
                e.FriendlyName = "Manual Time 1PPS Phase Adjust";
                e.ReadWrite = EpsilonCommandAccessType.Write;
                e.Write_Conditions = EpsilonCommandWriteConditions.Remote_Only_Holdover;
                Epsilon_Command_List.Add((int)e.Command_Index, e);

                e = new EpsilonCommandInfo();
                e.Command_Index = EpsilonCommandsIndex.Remote_Control_Write;
                e.Payload_Size = 1;
                e.FriendlyName = "Remote Control Enable";
                e.ReadWrite = EpsilonCommandAccessType.Write;
                e.Write_Conditions = EpsilonCommandWriteConditions.Always_Allowed;
                Epsilon_Command_List.Add((int)e.Command_Index, e);

                e = new EpsilonCommandInfo();
                e.Command_Index = EpsilonCommandsIndex.Phase_Correction_Write;
                e.Payload_Size = 4;
                e.FriendlyName = "Antenna Phase Correction";
                e.ReadWrite = EpsilonCommandAccessType.Write;
                e.Write_Conditions = EpsilonCommandWriteConditions.Remote_Must_Be_Set;
                Epsilon_Command_List.Add((int)e.Command_Index, e);

                e = new EpsilonCommandInfo();
                e.Command_Index = EpsilonCommandsIndex.Leap_Second_Write;
                e.Payload_Size = 6;
                e.FriendlyName = "Leap Second Information";
                e.ReadWrite = EpsilonCommandAccessType.Write;
                e.Write_Conditions = EpsilonCommandWriteConditions.Remote_Must_Be_Set;
                Epsilon_Command_List.Add((int)e.Command_Index, e);

                e = new EpsilonCommandInfo();
                e.Command_Index = EpsilonCommandsIndex.Reset_Clock_Write;
                e.Payload_Size = 0;
                e.FriendlyName = "Clock Soft Reset";
                e.ReadWrite = EpsilonCommandAccessType.Write;
                e.Write_Conditions = EpsilonCommandWriteConditions.Always_Allowed;
                Epsilon_Command_List.Add((int)e.Command_Index, e);

                e = new EpsilonCommandInfo();
                e.Command_Index = EpsilonCommandsIndex.Error_Code;
                e.Payload_Size = 0;
                e.FriendlyName = "Error Code";
                e.ReadWrite = EpsilonCommandAccessType.N_A;
                e.Write_Conditions = EpsilonCommandWriteConditions.Always_Allowed;
                Epsilon_Command_List.Add((int)e.Command_Index, e);


                // documented in series 1 and 3 manual

                e = new EpsilonCommandInfo();
                e.Command_Index = EpsilonCommandsIndex.GPS_Init_Time_Read;
                e.Payload_Size = 7;
                e.FriendlyName = "GPS Receiver Time";
                e.ReadWrite = EpsilonCommandAccessType.Read;
                e.Write_Conditions = EpsilonCommandWriteConditions.Always_Allowed;
                Epsilon_Command_List.Add((int)e.Command_Index, e);

                e = new EpsilonCommandInfo();
                e.Command_Index = EpsilonCommandsIndex.GPS_Init_Time_Write;
                e.Payload_Size = 7;
                e.FriendlyName = "GPS Receiver Time";
                e.ReadWrite = EpsilonCommandAccessType.Write;
                e.Write_Conditions = EpsilonCommandWriteConditions.Remote_Must_Be_Set;
                Epsilon_Command_List.Add((int)e.Command_Index, e);
                
            }
        }







        public class EpsilonVersionMessage
        {
            public byte Software_Version;
            public byte Update_Version;
            public byte Clock_Series_No;
            public PowerInputTypes Power_24V; // if no then 48V
            public FrequencyInputTypes TimeSource; // if no then STANAG
            public ClockOutputTypes Clock_Output_Type;
            private bool Output_Frequency_1MHz; // if not then 2048 kHz
            private byte Output_Frequency_; // 0-3
            public FrequencyOutput Output_Frequency;
            public Oscillator_Types Oscillator_Type;
            public bool Relay_On_PhFreq_Limit;
            public EpsilonCommandsIndex ReadCommand;
            //public EpsilonCommandInfo WriteCommand;
            public bool DataValid;

            public enum FrequencyInputTypes
            {
                InputTypeGPS,
                InputTypeSTANAG
            };

            public enum PowerInputTypes
            {
                DCPower24V,
                DCPower48V
            };

            public enum ClockOutputTypes : byte
            {
                NO_OUTPUT = 7,
                RESERVED = 6,
                STANAG_4440 = 3,
                G704 = 2,
                Pulse_Rate = 1,
                IRIG_B = 0
            };

            public enum Oscillator_Types : byte
            {
                Unknown,
                Series3_Rubidium = 3,
                Series2_Rubidium = 2,
                OCXO_High_Performance = 1,
                OCXO_Standard = 0
            }

            public enum FrequencyOutput
            {
                Frequency_1MHz,
                Frequency_5MHz,
                Frequency_10MHz,
                Frequency_2048kHz,
                Frequency_4096kHz,
                Frequency_8192kHz,
                Frequency_Reserved
            };

            public bool ProcessMessage(EpsilonSerialMessage currentmessage)
            {
                if (currentmessage.MessageID != (byte)ReadCommand)
                {
                    return false;
                }

                Software_Version = currentmessage.Payload[4];
                Update_Version = currentmessage.Payload[5];



                // it looks like the data sheet swaps char 8 and 9 for this command
                // yup it's definitely swapped

                Clock_Series_No = (byte)(currentmessage.Payload[9] & 0b00000011);
                Power_24V = (currentmessage.Payload[9] & 0b00000100) > 0 ? PowerInputTypes.DCPower24V : PowerInputTypes.DCPower48V;
                TimeSource = (currentmessage.Payload[9] & 0b00001000) > 0 ? FrequencyInputTypes.InputTypeGPS : FrequencyInputTypes.InputTypeSTANAG;

                switch ((currentmessage.Payload[9] >> 4 & 0b00000111))
                {
                    case 0:
                        Clock_Output_Type = ClockOutputTypes.IRIG_B;
                        break;
                    case 1:
                        Clock_Output_Type = ClockOutputTypes.Pulse_Rate;
                        break;
                    case 2:
                        Clock_Output_Type = ClockOutputTypes.G704;
                        break;
                    case 3:
                        Clock_Output_Type = ClockOutputTypes.STANAG_4440;
                        break;
                    case 7:
                        Clock_Output_Type = ClockOutputTypes.NO_OUTPUT;
                        break;
                    default:
                        Clock_Output_Type = ClockOutputTypes.RESERVED;
                        break;

                }

                Output_Frequency_1MHz = (byte)(currentmessage.Payload[8] & 0b10000000) > 0;

                Output_Frequency_ = (byte)(currentmessage.Payload[8] & 0b00000011);

                switch (Output_Frequency_)
                {
                    case 1:
                        Output_Frequency = Output_Frequency_1MHz ? FrequencyOutput.Frequency_10MHz : FrequencyOutput.Frequency_8192kHz;

                        break;
                    case 2:
                        Output_Frequency = Output_Frequency_1MHz ? FrequencyOutput.Frequency_5MHz : FrequencyOutput.Frequency_4096kHz;
                        break;
                    case 3:
                        Output_Frequency = Output_Frequency_1MHz ? FrequencyOutput.Frequency_1MHz : FrequencyOutput.Frequency_2048kHz;
                        break;
                    default:
                        Output_Frequency = FrequencyOutput.Frequency_Reserved;
                        break;
                }

                

                switch ((currentmessage.Payload[8] >> 2 & 0b00000011))
                {
                    case 0:
                        Oscillator_Type = Oscillator_Types.OCXO_Standard;
                        break;
                    case 1:
                        Oscillator_Type = Oscillator_Types.OCXO_High_Performance;
                        break;
                    case 2:
                        Oscillator_Type = Oscillator_Types.Series2_Rubidium;
                        break;
                    case 3:
                        Oscillator_Type = Oscillator_Types.Series3_Rubidium;
                        break;
                    default:
                        DataValid = false;
                        break;

                }



                Relay_On_PhFreq_Limit = (byte)(currentmessage.Payload[8] & 0b00010000) == 0;

                DataValid = true;
                return DataValid;
            }
        }



        public class EpsilonGPSInformation
        {
            public GPSPositioningModes Positioning_Mode;
            public Int32 GPS_Latitude_ms;
            public Int32 GPS_Longtitude_ms;
            public Int32 GPS_Altitude_cm;
            private GeoCoordinate GPS_Position_;
            public GeoCoordinate GPS_Position
            {
                set
                {
                    GPS_Position_ = value;
                    GPS_Latitude_ms = Convert.ToInt32(value.Latitude * GPS_Conversion_Factor);
                    GPS_Longtitude_ms = Convert.ToInt32(value.Longitude * GPS_Conversion_Factor);
                    GPS_Altitude_cm = Convert.ToInt32(value.Altitude * 100);
                }
                get
                {
                    return GPS_Position_;
                }
            }
            public GPSTimeReferenceModes Time_Reference;
            public EpsilonCommandsIndex ReadCommand;
            public EpsilonCommandsIndex WriteCommand;
            public bool DataValid = false;

            private static double GPS_Conversion_Factor = 3600000d;

            public enum GPSPositioningModes : byte
            {
                Positioning_Mode_Automatic = 1,
                Positioning_Mode_Manual = 2,
                Positioning_Mode_Mobile = 3,
            };

            public enum GPSTimeReferenceModes : byte
            {
                Time_Mode_GPS = 0,
                Time_Mode_UTC = 1
            }

            public EpsilonGPSInformation()
            {
                ReadCommand = EpsilonCommandsIndex.GPS_Init_Read;
                WriteCommand = EpsilonCommandsIndex.GPS_Init_Write;
            }

            public bool ProcessMessage(EpsilonSerialMessage currentmessage)
            {
                if (currentmessage.MessageID != (byte)ReadCommand)
                {
                    return false;
                }
                if (currentmessage.Payload[0] > 0 && currentmessage.Payload[0] <= 3)
                {
                    Positioning_Mode = (GPSPositioningModes)currentmessage.Payload[0];
                    //comboBox2.SelectedIndex = (byte)lastGPSinfo.Positioning_Mode - 1;
                    //comboBox2.Enabled = true;
                }
                else
                {
                    return false;
                }


                GPS_Latitude_ms = (Int32)currentmessage.Payload[1] << 24 | (Int32)currentmessage.Payload[2] << 16 |
                    (Int32)currentmessage.Payload[3] << 8 | (Int32)currentmessage.Payload[4];

                GPS_Longtitude_ms = (Int32)currentmessage.Payload[5] << 24 | (Int32)currentmessage.Payload[6] << 16 |
                    (Int32)currentmessage.Payload[7] << 8 | (Int32)currentmessage.Payload[8];

                GPS_Altitude_cm = (Int32)currentmessage.Payload[9] << 24 | (Int32)currentmessage.Payload[10] << 16 |
                    (Int32)currentmessage.Payload[11] << 8 | (Int32)currentmessage.Payload[12];

                GPS_Position_ = new GeoCoordinate(GPS_Latitude_ms / GPS_Conversion_Factor, GPS_Longtitude_ms / GPS_Conversion_Factor, GPS_Altitude_cm / 100.0d);

                Time_Reference = currentmessage.Payload[18] == 1 ?
                    GPSTimeReferenceModes.Time_Mode_UTC : GPSTimeReferenceModes.Time_Mode_GPS;


                DataValid = true;
                return true;
            }

            public bool Serialize(out List<Byte> Payload)
            {
                bool retval = true;

                Payload = new List<byte>();

                

                Payload.Add((byte)Positioning_Mode);

                if (Positioning_Mode == GPSPositioningModes.Positioning_Mode_Manual)
                {
                    // latitude
                    byte[] latitude = BitConverter.GetBytes(GPS_Latitude_ms);
                    if (BitConverter.IsLittleEndian)
                        Array.Reverse(latitude);

                    // longtitude
                    byte[] longtitude = BitConverter.GetBytes(GPS_Longtitude_ms);
                    if (BitConverter.IsLittleEndian)
                        Array.Reverse(longtitude);

                    // altitude
                    byte[] altitude = BitConverter.GetBytes(GPS_Altitude_cm);
                    if (BitConverter.IsLittleEndian)
                        Array.Reverse(altitude);

                    // add the position
                    Payload.AddRange(latitude);
                    Payload.AddRange(longtitude);
                    Payload.AddRange(altitude);
                }
                else
                {
                    Payload.AddRange(new byte[4*3]);
                }


                Payload.AddRange(new byte[5]);


                Payload.Add((byte)Time_Reference);

                return retval;
            }


        };

        

        public class EpsilonStatusMessageResponse
        {

            public bool Clock_Synchronized;
            public bool GPS_1PPS_Failure;
            public bool Frequency_Driver_Failure;
            public bool Failure_1PPS_Driver;
            public bool Frequency_Output_Failure;
            public bool Failure_1PPS_Output;
            public bool Phase_Limit_Alarm;
            public bool Frequency_Limit_Alarm;
            public bool Option_Board_Output_Failure;
            public bool Epsilon_Hardware_Failure;
            public bool Antenna_Not_Connected;
            public bool Antenna_Short_Circuit;
            public bool Frequency_Output_Locked;
            public GPSReceptionMode GPS_Position_Type;
            //private byte[] GPS_DATA; // 15 bytes of GPS data
            public List<SatelliteStatus> SatelliteList;
            public int SatelliteList_Valid_Count;
            public UInt16 Standard_Deviation_1PPS; // ns deviation
            public Int32 GPS_Latitude_ms;
            public Int32 GPS_Longtitude_ms;
            public Int32 GPS_Altitude_cm;
            public GeoCoordinate GPS_Position;


            public bool GPS_Receiver_Failure;

            public EpsilonCommandsIndex ReadCommand;
            //EpsilonCommandInfo WriteCommand;
            public bool DataValid;

            public enum GPSReceptionMode { GPS_Reception_0D, GPS_Reception_2D_4to8, GPS_Reception_3D_4to8, GPS_Reception_Unknown };

            public EpsilonStatusMessageResponse()
            {
                SatelliteList = new List<SatelliteStatus>(8);
                GPS_Position = new GeoCoordinate();
                SatelliteList_Valid_Count = 0;
                DataValid = false;
            }

            public bool ProcessMessage(EpsilonSerialMessage currentmessage)
            {
                if (currentmessage.MessageID != (byte)ReadCommand)
                {
                    return false;
                }
                //laststatus = new StatusMessageResponse();
                // process byte 0..3
                UInt32 currentlong;
                currentlong = (UInt32)currentmessage.Payload[0] << 24 | (UInt32)currentmessage.Payload[1] << 16 |
                    (UInt32)currentmessage.Payload[2] << 8 | (UInt32)currentmessage.Payload[3];
                Clock_Synchronized = (currentlong & 1 << 0) > 0;
                GPS_1PPS_Failure = (currentlong & 1 << 8) > 0;
                Frequency_Driver_Failure = (currentlong & 1 << 9) > 0;
                Failure_1PPS_Driver = (currentlong & 1 << 10) > 0;
                Frequency_Output_Failure = (currentlong & 1 << 11) > 0;
                Failure_1PPS_Output = (currentlong & 1 << 12) > 0;
                Phase_Limit_Alarm = (currentlong & 1 << 13) > 0;
                Frequency_Limit_Alarm = (currentlong & 1 << 14) > 0;
                Option_Board_Output_Failure = (currentlong & 1 << 15) > 0;
                Epsilon_Hardware_Failure = (currentlong & 1 << 16) > 0;
                Antenna_Not_Connected = (currentlong & 1 << 18) > 0;
                Antenna_Short_Circuit = (currentlong & 1 << 19) > 0;
                Frequency_Output_Locked = (currentlong & 1 << 24) > 0;

                // process byte 4
                if (currentmessage.Payload[4] == 1 || currentmessage.Payload[4] == 5)
                {
                    GPS_Position_Type = GPSReceptionMode.GPS_Reception_0D;
                }
                else if (currentmessage.Payload[4] == 2 || currentmessage.Payload[4] == 6)
                {
                    GPS_Position_Type = GPSReceptionMode.GPS_Reception_2D_4to8;
                }
                else if (currentmessage.Payload[4] == 3 || currentmessage.Payload[4] == 7)
                {
                    GPS_Position_Type = GPSReceptionMode.GPS_Reception_3D_4to8;
                }
                else
                {
                    GPS_Position_Type = GPSReceptionMode.GPS_Reception_Unknown;
                }

                // process byte 5..20 (deal with this later...)
                List<UInt16> GPS_DATA = new List<ushort>(8);
                GPS_DATA.Add((UInt16)(currentmessage.Payload[5] | currentmessage.Payload[6] << 8));
                GPS_DATA.Add((UInt16)(currentmessage.Payload[7] | currentmessage.Payload[8] << 8));
                GPS_DATA.Add((UInt16)(currentmessage.Payload[9] | currentmessage.Payload[10] << 8));
                GPS_DATA.Add((UInt16)(currentmessage.Payload[11] | currentmessage.Payload[12] << 8));
                GPS_DATA.Add((UInt16)(currentmessage.Payload[13] | currentmessage.Payload[14] << 8));
                GPS_DATA.Add((UInt16)(currentmessage.Payload[15] | currentmessage.Payload[16] << 8));
                GPS_DATA.Add((UInt16)(currentmessage.Payload[17] | currentmessage.Payload[18] << 8));
                GPS_DATA.Add((UInt16)(currentmessage.Payload[19] | currentmessage.Payload[20] << 8));

                _Set_GPS_Data(GPS_DATA.ToArray());
                //laststatus.GPS_DATA = currentmessage.data.GetRange(2, 15).ToArray();

                // process byte 21..22
                Standard_Deviation_1PPS = (UInt16)((UInt16)currentmessage.Payload[21] << 8 | (UInt16)currentmessage.Payload[22]);

                // process byte 23..26
                GPS_Latitude_ms = (Int32)currentmessage.Payload[23] << 24 | (Int32)currentmessage.Payload[24] << 16 |
                    (Int32)currentmessage.Payload[25] << 8 | (Int32)currentmessage.Payload[26];
                // process byte 27..30
                GPS_Longtitude_ms = (Int32)currentmessage.Payload[27] << 24 | (Int32)currentmessage.Payload[28] << 16 |
                    (Int32)currentmessage.Payload[29] << 8 | (Int32)currentmessage.Payload[30];
                // process byte 31..34
                GPS_Altitude_cm = (Int32)currentmessage.Payload[31] << 24 | (Int32)currentmessage.Payload[32] << 16 |
                    (Int32)currentmessage.Payload[33] << 8 | (Int32)currentmessage.Payload[34];

                GPS_Position = new GeoCoordinate(GPS_Latitude_ms / 3600000d, GPS_Longtitude_ms / 3600000d, GPS_Altitude_cm / 100.0d);

                // process byte 35
                GPS_Receiver_Failure = (currentmessage.Payload[35] & 1 << 0) > 0;

                DataValid = true;
                return true;
            }

            private void _Set_GPS_Data(UInt16[] GPS_DATA_)
            {
                SatelliteList_Valid_Count = 0;
                SatelliteList.Clear();
                foreach (UInt16 b in GPS_DATA_)
                {
                    SatelliteStatus s = new SatelliteStatus();
                    byte lowerb = (byte)(b & 0xff);
                    byte upperb = (byte)(b >> 8 & 0xff);
                    s.Tracking = (lowerb & 0b1000000) == 0;
                    s.SatelliteNo = (byte)(lowerb & 0b01111111);
                    s.SNR = upperb;
                    if (s.SatelliteNo != 0 && s.SNR > 0)
                    {
                        s.Valid = true;
                        SatelliteList_Valid_Count++;
                    }
                    else
                    {
                        s.Valid = false;
                    }
                    SatelliteList.Add(s);

                    // sort list by satellite no. and tracking status
                    //SatelliteList.Sort((x, y) => x.SatelliteNo.CompareTo(y.Valid));
                    SatelliteList = SatelliteList.OrderBy(o => o.Tracking).ThenBy(o => o.SatelliteNo).ToList();
                }
            }

        };


        // information about the leap second, if scheduled
        class EpsilonLeapStatus
        {
            public bool LeapSecondUsed;
            public bool Add;
            public UInt16 Day;
            public UInt16 Year;
            public EpsilonCommandsIndex ReadCommand;
            public EpsilonCommandsIndex WriteCommand;
            public bool DataValid;


            public bool ProcessMessage(EpsilonSerialMessage currentmessage)
            {
                if (currentmessage.MessageID != (byte)ReadCommand)
                {
                    return false;
                }

                LeapSecondUsed = currentmessage.Payload[0] == 1 ? false : true;
                Add = currentmessage.Payload[1] == 1 ? true : false;
                Day = (UInt16)((UInt16)currentmessage.Payload[2] << 8 | (UInt16)currentmessage.Payload[3]);
                Year = (UInt16)((UInt16)currentmessage.Payload[4] << 8 | (UInt16)currentmessage.Payload[5]);

                DataValid = true;
                return DataValid;
            }
        }

        public class EpsilonSerialMessage
        {
            public byte MessageID;
            public byte PayloadLength;
            //public byte[] data;
            public List<byte> Payload;
            public List<byte> RawData;
            public byte checksum;

            public EpsilonSerialMessage()
            {
                MessageID = 0;
                checksum = 0;
                Payload = new List<byte>();
                PayloadLength = 0;
                RawData = new List<byte>();
            }
        };
    }

}
