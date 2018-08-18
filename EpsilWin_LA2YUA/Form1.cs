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
using System.Diagnostics;

namespace EpsilWin_LA2YUA
{

    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();

            string Version = "0.4";
            string Date = "2018";

            label1.Text = String.Format("LA2YUA Epsilon Clock Interface EC2S, version {0} - {1}", Version, Date);
            this.Text = label1.Text;

            toolStripStatusLabel1.Text = "";
            //toolStripStatusLabel1.Update();

            Update_COM_List(true);

            //Populate_Epsilon_Command_List();

            System.IO.Stream myStream = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("EpsilWin_LA2YUA.Resources.infotext.rtf");

            //StringReader rtfreader 

            richTextBox1.LoadFile(myStream, RichTextBoxStreamType.RichText);

            epsilondevice = new EpsilonDeviceContext();

        }


        EpsilonDeviceContext epsilondevice;

        // request a read of any command (command must be listed in the command list)
        // payload is optional for read commands
        void Epsilon_Issue_Command(EpsilonCommandsIndex command, List<byte> payload = null)
        {
            // look up command information
            EpsilonCommandInfo e;
            if (!epsilondevice.Epsilon_Command_List.TryGetValue((int)command, out e))
            {
                richTextBox1_Serial_Log.AppendText("Transmit: unknown command.\n");
                return;
            }

           //Debug.Assert(command == e.Command_Index);
           if (command != e.Command_Index)
            {
                throw new Exception("Command lookup returned wrong command ID. World is upside down.");
            }

            // TODO: Check if requirements to transmit are met, largely not important since the device just NACKs if there's something wrong
            /*switch (e.Write_Conditions)
            {
                case Epsilon_Write_Command_Conditions.Always_Allowed:
                    break;
                case Epsilon_Write_Command_Conditions.Remote_Must_Be_Set:
                    if (checkBox1.CheckState != CheckState.Checked)
                    {
                        richTextBox1_Serial_Log.AppendText("Check\n");
                    }
                    break;
            }*/

            EpsilonSerialMessage txmessage = new EpsilonSerialMessage();

            txmessage.MessageID = (byte)e.Command_Index;
            txmessage.PayloadLength = e.Payload_Size;

            // check if it's a read or write command
            // 
            if (e.ReadWrite == EpsilonCommandAccessType.Read)
            { 
                byte[] tmp = new byte[txmessage.PayloadLength];
                txmessage.Payload.AddRange(tmp);
            }
            else if (payload.Count != e.Payload_Size)
            {
                richTextBox1_Serial_Log.AppendText("Transmit: payload size mismatch\n");
                return;
            }
            else if (payload != null)
            {
                txmessage.Payload.AddRange(payload);
            }
            else
            {
                richTextBox1_Serial_Log.AppendText("Transmit: attempted to send garbage data\n");
                return;
            }

            // special case: alarm read requires a bit to be set
            // to determine if 0.1 or 1ppb resolution is available
            if (command == EpsilonCommandsIndex.Alarm_Limits_Read)
            {
                txmessage.Payload[4] |= 0b10000000;
            }

            serialData_Process_Tx(txmessage);
        }


        bool received_data = false;


        private void dataGridView1_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {

        }

        private void button_Connect_Serial_Click(object sender, EventArgs e)
        {
            if (serialPort1.IsOpen)
            {
                serialPort1.Close();
                richTextBox1_Serial_Log.AppendText(String.Format("Closing port {0}\n", serialPort1.PortName));
                button_Connect_Serial.Text = "Connect";
                button_Connect_Serial.BackColor = default(Color);
                button_Connect_Serial.UseVisualStyleBackColor = true;
                received_data = false;
                return;
            }

            Try_Open_Serial_Port();

            if (serialPort1.IsOpen)
            {
                button_Connect_Serial.Text = "Disconnect";
            }
        }

        private bool Try_Open_Serial_Port()
        {
            if (serialPort1.IsOpen)
            {
                serialPort1.Close();
                richTextBox1_Serial_Log.AppendText(String.Format("Closing port {0}\n", serialPort1.PortName));
            }

            if (comboBox1_Serial_Port.SelectedItem == null)
            {
                richTextBox1_Serial_Log.AppendText("Please select a COM port first.\n");
                return false;
            }

            // Try to open COM port
            try
            {
                serialPort1.PortName = comboBox1_Serial_Port.SelectedItem.ToString();

                richTextBox1_Serial_Log.AppendText(String.Format("Opening {0}, {1}-{2}{3}{4}\n", 
                    serialPort1.PortName, 
                    serialPort1.BaudRate, 
                    serialPort1.DataBits, 
                    serialPort1.Parity.ToString().Substring(0,1), 
                    Convert.ToInt32(serialPort1.StopBits)));
                serialPort1.Open();
            }
            catch (InvalidOperationException exc)
            {
                // port already open
                return true;
            }
            catch (Exception exc)
            {
                richTextBox1_Serial_Log.AppendText("Error opening serial port: " + exc.Message + "\n");
                return false;
            }
            richTextBox1_Serial_Log.AppendText("Port opened\n");

            button_Connect_Serial.BackColor = Color.Orange;
            return true;
        }

        private void timer1_Maintenance_Tick(object sender, EventArgs e)
        {
            timer1_Maintenance.Interval = 1000;
            Update_COM_List();

        }

        private void Update_COM_List(bool startup = false)
        {
            // 1 Hz timer
            // check if new serial ports were added or removed

            //ComboBox.ObjectCollection old_serial_list = comboBox1_Serial_Port.Items;

            bool list_outdated = false;
            foreach (string curport in SerialPort.GetPortNames())
            {
                if (!comboBox1_Serial_Port.Items.Contains(curport))
                {
                    list_outdated = true;
                }
            }

            if (comboBox1_Serial_Port.Items.Count != SerialPort.GetPortNames().Length)
            {
                list_outdated = true;
            }

            if (list_outdated)
            {

                string old_selection = String.Empty;

                if (comboBox1_Serial_Port.Items.Count > 0)
                {
                    old_selection = comboBox1_Serial_Port.Items[comboBox1_Serial_Port.SelectedIndex].ToString();
                }

                int new_selection = 0;
                // populate serial port list
                comboBox1_Serial_Port.Items.Clear();
                foreach (string curport in SerialPort.GetPortNames())
                {
                    comboBox1_Serial_Port.Items.Add(curport);
                    if (old_selection == curport && !String.IsNullOrEmpty(old_selection))
                    {
                        new_selection = comboBox1_Serial_Port.Items.Count - 1;
                    }
                }


                if (comboBox1_Serial_Port.Items.Count > 0)
                {
                    string stored_port = (string)Properties.Settings.Default["Selected_COM_Port"];
                    if (comboBox1_Serial_Port.Items.Contains(stored_port) && startup)
                    {
                        comboBox1_Serial_Port.SelectedIndex = comboBox1_Serial_Port.Items.IndexOf(stored_port);
                    }
                    else
                    {
                        // select index 0
                        comboBox1_Serial_Port.SelectedIndex = 0;
                    }
                    
                }
            }
        }

        private void Update_Version( EpsilonVersionMessage version)
        {
            dataGridView2_Version_Info.Rows.Clear();

            dataGridView2_Version_Info.Rows.Add(new object[] { "Software Version",
                String.Format("V{0}U{1}", version.Software_Version, version.Update_Version)});

            string statustext = "";

            dataGridView2_Version_Info.Rows.Add(new object[] { "Series Number",
                String.Format("Series {0}", version.Clock_Series_No)});

            dataGridView2_Version_Info.Rows.Add(new object[] { "DC Power",
                version.Power_24V == EpsilonVersionMessage.PowerInputTypes.DCPower24V ? "24V DC" : "48V DC"});

            dataGridView2_Version_Info.Rows.Add(new object[] { "Timing Source",
                version.TimeSource == EpsilonVersionMessage.FrequencyInputTypes.InputTypeGPS ? "GPS" : "STANAG"});

            switch (version.Clock_Output_Type)
            {
                case EpsilonVersionMessage.ClockOutputTypes.G704:
                    statustext = "G.704 Output";
                    break;
                case EpsilonVersionMessage.ClockOutputTypes.IRIG_B:
                    statustext = "IRIG.B Output";
                    break;
                case EpsilonVersionMessage.ClockOutputTypes.NO_OUTPUT:
                    statustext = "No Option";
                    break;
                case EpsilonVersionMessage.ClockOutputTypes.Pulse_Rate:
                    statustext = "Pulse Rate Output";
                    break;
                case EpsilonVersionMessage.ClockOutputTypes.RESERVED:
                    statustext = "Reserved (Unknown)";
                    break;
                case EpsilonVersionMessage.ClockOutputTypes.STANAG_4440:
                    statustext = "STANAG 4430 (Extended Have Quick, XHQ)";
                    break;

            }

            dataGridView2_Version_Info.Rows.Add(new object[] { "Output Option Type",
                statustext});


            switch (version.Output_Frequency)
            {
                case EpsilonVersionMessage.FrequencyOutput.Frequency_1MHz:
                    statustext = "1 MHz";
                    break;
                case EpsilonVersionMessage.FrequencyOutput.Frequency_5MHz:
                    statustext = "5 MHz";
                    break;
                case EpsilonVersionMessage.FrequencyOutput.Frequency_10MHz:
                    statustext = "10 MHz";
                    break;
                case EpsilonVersionMessage.FrequencyOutput.Frequency_2048kHz:
                    statustext = "2048 kHz";
                    break;
                case EpsilonVersionMessage.FrequencyOutput.Frequency_4096kHz:
                    statustext = "4096 kHz";
                    break;
                case EpsilonVersionMessage.FrequencyOutput.Frequency_8192kHz:
                    statustext = "8192 kHz";
                    break;
                case EpsilonVersionMessage.FrequencyOutput.Frequency_Reserved:
                    statustext = "Reserved";
                    break;
            }

            dataGridView2_Version_Info.Rows.Add(new object[] { "Output Frequency",
                statustext});

            switch (version.Oscillator_Type)
            {
                case EpsilonVersionMessage.Oscillator_Types.OCXO_High_Performance:
                    statustext = "OCXO, High Performance";
                    break;
                case EpsilonVersionMessage.Oscillator_Types.OCXO_Standard:
                    statustext = "OCXO, Standard";
                    break;
                case EpsilonVersionMessage.Oscillator_Types.Series2_Rubidium:
                    statustext = "Rubidium, Series 2 Standard";
                    break;
                case EpsilonVersionMessage.Oscillator_Types.Series3_Rubidium:
                    statustext = "Rubidium, Series 3 High Performance";
                    break;

            }

            dataGridView2_Version_Info.Rows.Add(new object[] { "Oscillator Type",
                statustext});

            dataGridView2_Version_Info.Rows.Add(new object[] { "Alarm Output Mode",
                version.Relay_On_PhFreq_Limit ? "Phase/Freq Limit + HW Issue" : "HW Issue Only"});
        }

        public struct SatelliteStatus
        {
            public bool Tracking;
            public byte SNR;
            public byte SatelliteNo;
            public bool Valid;
        }

        private void Update_Status(EpsilonStatusMessageResponse status)
        {
            string statustext = "";
            string statusbit = "OK";

            dataGridView1_Status_Info.Rows.Clear();
            dataGridView1_Status_Info.Rows.Add(new object[] { "Synchronization",
                status.Clock_Synchronized ? "Synchronized to GPS" : 
                "Clock is not synchronized. Holdover or initializing.",
            status.Clock_Synchronized ? "OK" : "ERROR"});

            dataGridView1_Status_Info.Rows.Add(new object[] { "GPS 1PPS",
                status.GPS_1PPS_Failure ? "GPS 1PPS Failure" : 
                "GPS 1PPS OK",
            status.GPS_1PPS_Failure ? "ERROR" : "OK"});

            dataGridView1_Status_Info.Rows.Add(new object[] { "Frequency Driver",
                status.Frequency_Driver_Failure ? "Frequency Driver Failure" :
                "Frequency Driver OK",
            status.Frequency_Driver_Failure ? "ERROR" : "OK"});

            dataGridView1_Status_Info.Rows.Add(new object[] { "1PPS Driver",
                status.Failure_1PPS_Driver ? "1PPS Driver Failure" :
                "1PPS Driver OK",
            status.Failure_1PPS_Driver ? "ERROR" : "OK"});

            dataGridView1_Status_Info.Rows.Add(new object[] { "Frequency Output",
                status.Frequency_Output_Failure ? "Frequency Output Failure" :
                "Frequency Output OK",
            status.Frequency_Output_Failure ? "ERROR" : "OK"});

            dataGridView1_Status_Info.Rows.Add(new object[] { "1PPS Output",
                status.Failure_1PPS_Output ? "1PPS Output Failure" :
                "1PPS Output OK",
            status.Failure_1PPS_Output ? "ERROR" : "OK"});

            dataGridView1_Status_Info.Rows.Add(new object[] { "Phase Limit",
                status.Phase_Limit_Alarm ? "Phase Limit Alarm" :
                "Phase Limit below alarm limit",
            status.Phase_Limit_Alarm ? "ERROR" : "OK"});

            dataGridView1_Status_Info.Rows.Add(new object[] { "Frequency Limit",
                status.Frequency_Limit_Alarm ? "Limit Alarm or Holdover" :
                "Frequency Limit below alarm limit",
            status.Frequency_Limit_Alarm ? "ERROR" : "OK"});

            dataGridView1_Status_Info.Rows.Add(new object[] { "Option Board",
                status.Option_Board_Output_Failure ? "Option Board Output Failure" :
                "Option Board OK",
            status.Option_Board_Output_Failure ? "ERROR" : "OK"});

            dataGridView1_Status_Info.Rows.Add(new object[] { "Hardware Status",
                status.Epsilon_Hardware_Failure ? "Hardware Failure" :
                "Hardware Operational",
            status.Epsilon_Hardware_Failure ? "ERROR" : "OK"});

            if (status.Antenna_Not_Connected || status.Antenna_Short_Circuit)
            {
                statustext = "";
                if (status.Antenna_Not_Connected && status.Antenna_Short_Circuit)
                {
                    statustext = "Not Connected AND Short Circuit (World is upside down, likely hardware fault)";
                }
                else
                {
                    statustext = status.Antenna_Not_Connected ? "Not Connected" : "Short Circuit";
                }

                dataGridView1_Status_Info.Rows.Add(new object[] { "Antenna",
                statustext,
                "ERROR"});
            }
            else
            {
                dataGridView1_Status_Info.Rows.Add(new object[] { "Antenna",
                "No Error",
                "OK"});
            }

            if (epsilondevice.LastVersionMessage.Software_Version >= 2)
            {
                dataGridView1_Status_Info.Rows.Add(new object[] { "10 MHz/1PPS Lock",
                status.Frequency_Output_Locked ? "10 MHz/1PPS in phase" :
                "10 MHz/1PPS phase not synced",
            status.Frequency_Output_Locked ? "OK" : "WARNING"});
            }
            else
            {
                dataGridView1_Status_Info.Rows.Add(new object[] { "10 MHz Lock",
                "Feature requires FW. V9r2",
            "N/A"});
            }
            

            // the position type is for some reason dependent on the GPS mode (in a different register!)
            // the GPS_Init state must be read before this to get correct results
            switch (status.GPS_Position_Type)
            {
                case EpsilonStatusMessageResponse.GPSReceptionMode.GPS_Reception_0D:
                    if (status.SatelliteList_Valid_Count > 1)
                    {
                        if (epsilondevice.GPS_Info.Positioning_Mode == EpsilonGPSInformation.GPSPositioningModes.Positioning_Mode_Automatic)
                        {
                            // after survey in auto this state will always be set (even for 4+ satellites tracked)
                            statustext = "Survey Complete (Auto)";
                            statusbit = "OK";
                        }
                        else if (epsilondevice.GPS_Info.Positioning_Mode == EpsilonGPSInformation.GPSPositioningModes.Positioning_Mode_Manual)
                        {
                             // we always get this state in manual mode
                            statustext = "Manual Position";
                            statusbit = "OK";
                        }
                        else
                        {
                             // in mobile mode this is a warning, since we only have a single satellite
                            statustext = "1D Fix, Mobile";
                            statusbit = "WARNING";
                        }
                    }
                    else
                    {
                         // in case we only have a single satellite tracked we consider this a warning in all modes
                        statustext = "Single Satellite";
                        statusbit = "WARNING";
                    }
                    break;
                case EpsilonStatusMessageResponse.GPSReceptionMode.GPS_Reception_2D_4to8:
                    if (epsilondevice.GPS_Info.Positioning_Mode == EpsilonGPSInformation.GPSPositioningModes.Positioning_Mode_Mobile)
                    {
                        // in mobile this indicates operation but a warning due to potentially poor track
                        statustext = "2D Fix (Mobile)";
                        statusbit = "WARNING";
                    }
                    else
                    {
                         // we assume that we can't survey with only 3 satellites in auto mode
                        statustext = "Survey Halted (2D Fix)";
                        statusbit = "WARNING";
                    }
                    break;
                case EpsilonStatusMessageResponse.GPSReceptionMode.GPS_Reception_3D_4to8:
                    if (epsilondevice.GPS_Info.Positioning_Mode == EpsilonGPSInformation.GPSPositioningModes.Positioning_Mode_Mobile)
                    {
                         // in mobile mode this is as good as it gets
                        statustext = "3D Fix (Mobile)";
                        statusbit = "OK";
                    }
                    else
                    {
                         // in auto mode a 3D fix will allow surveying
                        statustext = "Surveying (3D Fix)";
                        statusbit = "WARNING";
                    }
                    break;
                case EpsilonStatusMessageResponse.GPSReceptionMode.GPS_Reception_Unknown:
                    statustext = "NO FIX INFO";
                    statusbit = "ERROR";
                    break;
            }

            dataGridView1_Status_Info.Rows.Add(new object[] { "GPS BIT",
                status.GPS_Receiver_Failure ? "Receiver Failure" :
                "Receiver OK",
            status.GPS_Receiver_Failure ? "ERROR" : "OK"});

            dataGridView1_Status_Info.Rows.Add(new object[] { "GPS Fix",
                statustext,
                statusbit});

            GeoCoordinate gpscoords;

            if (epsilondevice.GPS_Info.Positioning_Mode == EpsilonGPSInformation.GPSPositioningModes.Positioning_Mode_Manual &&
                epsilondevice.GPS_Info.DataValid)
            {
                gpscoords = epsilondevice.GPS_Info.GPS_Position;
            }
            else
            {
                gpscoords = status.GPS_Position;
            }

                dataGridView1_Status_Info.Rows.Add(new object[] { "GPS Lat",
                String.Format("{0:N6}°", gpscoords.Latitude),
                "N/A"});

            dataGridView1_Status_Info.Rows.Add(new object[] { "GPS Long",
                String.Format("{0:N6}°", gpscoords.Longitude),
                "N/A"});

            dataGridView1_Status_Info.Rows.Add(new object[] { "GPS Altitude",
                String.Format("{0:N2} m", gpscoords.Altitude),
                "N/A"});

            

            dataGridView1_Status_Info.Rows.Add(new object[] { "1PPS Standard Dev.",
                String.Format("{0} ns", status.Standard_Deviation_1PPS),
                "N/A"});

            foreach(SatelliteStatus s in status.SatelliteList)
            {
                if (s.Valid)
                {
                    dataGridView1_Status_Info.Rows.Add(new object[] { String.Format("Satellite Track Ch. {0}",
                        status.SatelliteList.IndexOf(s)+1),
                    String.Format("PN {0}. SNR {1}", s.SatelliteNo.ToString().PadLeft(2, '0'), s.SNR.ToString().PadLeft(3, '0')),
                    s.Tracking ? "Tracked" : "Not Tracked"});
                }
            }


            int rowscount = dataGridView1_Status_Info.Rows.Count;

            for (int i = 0; i < rowscount; i++)
            {
                if (((string)dataGridView1_Status_Info.Rows[i].Cells[2].Value == "OK") ||
                    ((string)dataGridView1_Status_Info.Rows[i].Cells[2].Value == "N/A") ||
                    ((string)dataGridView1_Status_Info.Rows[i].Cells[2].Value == "Tracked"))
                {
                    dataGridView1_Status_Info.Rows[i].Cells[2].Style.BackColor = Color.LightGreen;
                }

                if ((string)(dataGridView1_Status_Info.Rows[i].Cells[2].Value) == "WARNING" ||
                    (string)(dataGridView1_Status_Info.Rows[i].Cells[2].Value) == "Not Tracked")
                {
                    dataGridView1_Status_Info.Rows[i].Cells[2].Style.BackColor = Color.LightGoldenrodYellow;
                }

                if ((string)(dataGridView1_Status_Info.Rows[i].Cells[2].Value) == "ERROR")
                {
                    dataGridView1_Status_Info.Rows[i].Cells[2].Style.BackColor = Color.Red;
                }
            }


        }


        enum serialRXState
        {
            serialRXState_Idle,
            serialRXState_Started,
            serialRXState_Escape,
            serialRxState_Stop
        };


        // callbacks for RX data
        delegate void SetSerialDataInputCallback(byte[] text);

        private serialRXState receiverstate = serialRXState.serialRXState_Idle;
        private int serialRXStateCounter = 0;
        private EpsilonSerialMessage currentmessage = new EpsilonSerialMessage();

        //private EpsilonStatusMessageResponse laststatus = new EpsilonStatusMessageResponse();
        //private EpsilonLeapStatus lastleapinfo = new EpsilonLeapStatus();
        //private EpsilonVersionMessage lastversioninfo = new EpsilonVersionMessage();
        //private EpsilonGPSInformation lastGPSinfo = new EpsilonGPSInformation();

        private void serialData_input(byte[] text)
        {
            // InvokeRequired required compares the thread ID of the
            // calling thread to the thread ID of the creating thread.
            // If these threads are different, it returns true.
            if (this.richTextBox1_Serial_Log.InvokeRequired)
            {
                SetSerialDataInputCallback d = new SetSerialDataInputCallback(serialData_input);
                this.Invoke(d, new object[] { text });
                return;
            }

            byte[] rxdata = text;
            StringBuilder sb = new StringBuilder();


            foreach(byte currentchar in rxdata)
            {
                switch((int)currentchar)
                {
                    case 2: // start byte
                        if (receiverstate != serialRXState.serialRXState_Escape)
                        {

                            serialRXStateCounter = 0;
                            currentmessage = new EpsilonSerialMessage();
                            
                            //currentmessage.RawData.Add(currentchar);
                        }
                        else
                        {
                            serialRXStateCounter++;
                        }

                        receiverstate = serialRXState.serialRXState_Started;

                        break;
                    case 16: // escape
                        if (receiverstate == serialRXState.serialRXState_Escape)
                        {
                            receiverstate = serialRXState.serialRXState_Started;
                            //currentmessage.Payload.Add(currentchar);
                            serialRXStateCounter++;
                        }
                        else
                        {
                            receiverstate = serialRXState.serialRXState_Escape;
                            
                        }
                        //currentmessage.RawData.Add(currentchar);
                        // don't increment?
                        break;
                    case 3:
                        if (receiverstate == serialRXState.serialRXState_Started)
                        {
                            receiverstate = serialRXState.serialRxState_Stop;
                            // flag end of message
                            break;

                        }
                        else if (receiverstate == serialRXState.serialRXState_Escape)
                        {
                            serialRXStateCounter++;
                            receiverstate = serialRXState.serialRXState_Started;
                        }
                        break;
                    default:
                        serialRXStateCounter++;
                        break;
                }

                currentmessage.RawData.Add(currentchar);

                if (receiverstate == serialRXState.serialRXState_Started)
                {
                    if (serialRXStateCounter == 0)
                    {

                    }
                    else if (serialRXStateCounter == 1)
                    {
                        currentmessage.MessageID = currentchar;
                    }
                    else if (serialRXStateCounter == 2)
                    {
                        currentmessage.PayloadLength = currentchar;
                        
                    }
                    else
                    {
                        // write entry
                        currentmessage.Payload.Add(currentchar);
                    }
                }
                else if (receiverstate == serialRXState.serialRxState_Stop)
                {
                    byte checksum_calculated = 0;
                    checksum_calculated ^= currentmessage.MessageID;
                    checksum_calculated ^= currentmessage.PayloadLength;

                    bool checksumvalid = false;

                    // get the received checksum
                    currentmessage.checksum = currentmessage.Payload.Last();

                    currentmessage.Payload.RemoveAt(currentmessage.Payload.Count - 1);
                    //currentmessage.RawData.RemoveAt(currentmessage.RawData.Count - 1);

                    foreach (byte currentbyte in currentmessage.Payload)
                    {
                        checksum_calculated ^= currentbyte;
                    }

                    if (checksum_calculated == currentmessage.checksum)
                    {
                        checksumvalid = true;

                        if (!received_data)
                        {
                            richTextBox1_Serial_Log.AppendText("Receiver: Received valid data\n");

                            button_Connect_Serial.BackColor = Color.LightGreen;
                            received_data = true;
                        }
                    }
                    else
                    {
                        receiverstate = serialRXState.serialRXState_Idle;
                        richTextBox1_Serial_Log.AppendText("Receiver: Checksum error\n");
                        break;
                    }

                    serialRXStateCounter = 0;


                    EpsilonCommandInfo e;
                    if (!epsilondevice.Epsilon_Command_List.TryGetValue(currentmessage.MessageID, out e))
                    {
                        e.Clone(out e);
                        e.Command_Index = EpsilonCommandsIndex.UNKNOWN_COMMAND;
                    }
                    else if (!epsilondevice.ProcessRXCommand(currentmessage))
                    {
                        //sb.AppendFormat("Receiver: Decode failed for message\n");
                        if (e.ReadWrite == EpsilonCommandAccessType.Read)
                        {
                            e.Clone(out e);
                            e.Command_Index = EpsilonCommandsIndex.DECODE_FAILED;
                        }
                    }

                    //UInt32 currentlong;
                    switch (e.Command_Index)
                    {
                        case EpsilonCommandsIndex.Alarm_Limit_Write:
                        case EpsilonCommandsIndex.Display_Write:
                        case EpsilonCommandsIndex.Force_Holdover_Write:
                        case EpsilonCommandsIndex.GPS_Init_Write:
                        case EpsilonCommandsIndex.Leap_Second_Write:
                        case EpsilonCommandsIndex.Local_Time_Write:
                        case EpsilonCommandsIndex.Manual_1PPS_Write:
                        case EpsilonCommandsIndex.Manual_Second_Write:
                        case EpsilonCommandsIndex.Manual_Time_Write:
                        case EpsilonCommandsIndex.Phase_Correction_Write:
                        case EpsilonCommandsIndex.Remote_Control_Write:
                        case EpsilonCommandsIndex.TOD_Period_Write:
                        case EpsilonCommandsIndex.TOD_Setup_Write:
                        case EpsilonCommandsIndex.Version_Write:

                            sb.AppendFormat("Received ACK for {0} to {1}\n", 
                                e.ReadWrite == EpsilonCommandAccessType.Read ? "Read" : "Write",
                                e.FriendlyName);

                            switch (e.Command_Index)
                            {
                                case EpsilonCommandsIndex.TOD_Period_Write:
                                    button3_Set_TOD_Interval.BackColor = Color.LightGreen;
                                    break;
                                case EpsilonCommandsIndex.Display_Write:
                                    button10_Set_Display_Format.BackColor = Color.LightGreen;
                                    break;
                                case EpsilonCommandsIndex.Alarm_Limit_Write:
                                    button12_Set_Alarm_Limit.BackColor = Color.LightGreen;
                                    break;
                                case EpsilonCommandsIndex.Remote_Control_Write:
                                    button5_Set_Remote.BackColor = Color.LightGreen;
                                    break;
                                case EpsilonCommandsIndex.GPS_Init_Write:
                                    button8_Set_GPS.BackColor = Color.LightGreen;
                                    break;
                                case EpsilonCommandsIndex.TOD_Setup_Write:
                                    button1_Set_TOD_Output.BackColor = Color.LightGreen;
                                    break;
                                case EpsilonCommandsIndex.Local_Time_Write:
                                    button15_Set_Local_Time.BackColor = Color.LightGreen;
                                    break;
                                case EpsilonCommandsIndex.Manual_1PPS_Write:
                                     button23_Set_1PPS_Ph.BackColor = Color.LightGreen;
                                    break;
                                case EpsilonCommandsIndex.Force_Holdover_Write:
                                    button18_Set_Holdover.BackColor = Color.LightGreen;
                                    break;
                                case EpsilonCommandsIndex.Manual_Time_Write:
                                    button16_Set_Time_Now.BackColor = Color.LightGreen;
                                    button17_Set_Man_Time.BackColor = Color.LightGreen;
                                    break;
                                case EpsilonCommandsIndex.Manual_Second_Write:
                                    button2_Set_Second_Step.BackColor = Color.LightGreen;
                                    break;
                                case EpsilonCommandsIndex.GPS_Init_Time_Write:
                                    button2.BackColor = Color.LightGreen;
                                    button3.BackColor = Color.LightGreen;
                                    break;
                                case EpsilonCommandsIndex.Manual_Frequency_Write:
                                    button4_Set_Freq_Manual.BackColor = Color.LightGreen;
                                    break;
                            }
                            break;
                        case EpsilonCommandsIndex.Remote_Control_Read: // Remote Control
                            checkBox1.CheckState = CheckState.Checked;
                            checkBox1.Checked = epsilondevice.RemoteControlAuthorized.RemoteAllowed;
                            checkBox1.Enabled = true;
                            button6__Get_Remote.BackColor = Color.LightGreen;
                            button5_Set_Remote.Enabled = true;
                            break;
                        case EpsilonCommandsIndex.Manual_Second_Read: // manual second correction
                            radioButton6_plus.Enabled = true;
                            radioButton7_minus.Enabled = true;
                            button4_Get_Step_Seconds.BackColor = Color.LightGreen;

                            if (epsilondevice.ManualSecondCorrection.CorrectionType == 
                                EpsilonManualSecondCorrection.EpsilonManualSecondCorrectionTypes.Add_1_Second)
                            {
                                radioButton6_plus.Checked = true;
                                radioButton7_minus.Checked = false;
                            }
                            else
                            {
                                radioButton6_plus.Checked = false;
                                radioButton7_minus.Checked = true;
                            }
                            button2_Set_Second_Step.Enabled = true;
                            break;
                        case EpsilonCommandsIndex.Manual_1PPS_Read: // 1PPS correction
                            numericUpDown8.Value = epsilondevice.Manual1PPS.PPSCorrectionValue;
                            numericUpDown8.Enabled = true;
                            button24_Get_1PPS_Phase.BackColor = Color.LightGreen;
                            button23_Set_1PPS_Ph.Enabled = true;
                            break;
                        case EpsilonCommandsIndex.Manual_Time_Read: // manual time
                            dateTimePicker2.Value = epsilondevice.ManualTimeSetting.ManualTime;
                            dateTimePicker2.MaxDate = epsilondevice.ManualTimeSetting.maximumtime;
                            dateTimePicker2.MinDate = epsilondevice.ManualTimeSetting.minimumtime;
                            dateTimePicker2.Enabled = true;
                            button20_Get_Manual_Time.BackColor = Color.LightGreen;
                            button17_Set_Man_Time.Enabled = true;
                            button16_Set_Time_Now.Enabled = true;
                            break;
                        case EpsilonCommandsIndex.GPS_Init_Time_Read: // manual time
                            dateTimePicker1_GPS_Time.MinDate = epsilondevice.GPSTimeInit.minimumtime;
                            dateTimePicker1_GPS_Time.MaxDate = epsilondevice.GPSTimeInit.maximumtime;
                            dateTimePicker1_GPS_Time.Value = epsilondevice.GPSTimeInit.ManualTime;
                            dateTimePicker1_GPS_Time.Enabled = true;
                            button1.BackColor = Color.LightGreen;
                            button2.Enabled = true;
                            button3.Enabled = true;
                            break;
                        case EpsilonCommandsIndex.Force_Holdover_Read: // Forced Holdover
                            checkBox2.CheckState = epsilondevice.Forced_Holdover.Forced_Holdover == false ? CheckState.Unchecked : CheckState.Checked;
                            //checkBox2.Checked = epsilondevice.Forced_Holdover.Forced_Holdover;
                            checkBox2.Enabled = true;
                            button19_Get_Force_Holdover.BackColor = Color.LightGreen;
                            button18_Set_Holdover.Enabled = true;
                            break;
                        case EpsilonCommandsIndex.Version_Read: // Version info
                            button2_Version_read.BackColor = Color.LightGreen;
                            Update_Version(epsilondevice.LastVersionMessage);
                            break;
                        case EpsilonCommandsIndex.Alarm_Limits_Read: // Alarm Limits
                            numericUpDown6.Value = epsilondevice.AlarmInfo.PhaseAlarmLimit;
                            numericUpDown6.Enabled = true;

                            
                            if (epsilondevice.AlarmInfo.AlarmAccuracy == 
                                EpsilonAlarmInformation.EpsilonAlarmAccuracy.AlarmResolution0_1ppb)
                            {
                                label21.Text = String.Format("Frequency Alarm Step is {0} ppb ({1} Hz at 10 MHz)", 0.1d, 0.001);
                                numericUpDown7.DecimalPlaces = 1;
                            }
                            else
                            {
                                label21.Text = String.Format("Frequency Alarm Step is {0} ppb ({1} Hz at 10 MHz)", 1d, 0.01);
                                numericUpDown7.DecimalPlaces = 0;
                            }
                            numericUpDown7.Value = epsilondevice.AlarmInfo.FrequencyAlarmLimit;

                            numericUpDown7.Enabled = true;

                            checkBox3.CheckState = CheckState.Checked;
                            checkBox3.Checked = epsilondevice.AlarmInfo.FrequencyOutputSquelch;
                            checkBox3.Enabled = true;

                            button13_Get_Alarm.BackColor = Color.LightGreen;
                            button12_Set_Alarm_Limit.Enabled = true;
                            break;
                        case EpsilonCommandsIndex.Display_Read:// Display
                            switch (epsilondevice.DisplayInfo.GetDisplayMode)
                            {
                                case EpsilonDisplay.DisplayMode.TOD_Format_1:
                                    radioButton1.Select();
                                    break;
                                case EpsilonDisplay.DisplayMode.TOD_Format_2:
                                    radioButton2.Select();
                                    break;
                                case EpsilonDisplay.DisplayMode.TOD_Format_3:
                                    radioButton3.Select();
                                    break;
                                case EpsilonDisplay.DisplayMode.TOD_Format_4:
                                    radioButton4.Select();
                                    break;
                                case EpsilonDisplay.DisplayMode.TOD_Format_5:
                                    radioButton5.Select();
                                    break;
                            }

                            radioButton1.Enabled = true;
                            radioButton2.Enabled = true;
                            radioButton3.Enabled = true;
                            radioButton4.Enabled = true;
                            radioButton5.Enabled = true;
                            button11_Get_Display.BackColor = Color.LightGreen;
                            button10_Set_Display_Format.Enabled = true;
                            break;
                        case EpsilonCommandsIndex.Leap_Second_Read: // Leap Second
                            EpsilonLeapStatus lastleapinfo = epsilondevice.Leap_Second_Information;

                            if (lastleapinfo.LeapSecondUsed)
                            {
                                label19.Text = String.Format("Leap scheduled for year {0} day {1}, will {2} 1 second.", 
                                    lastleapinfo.Year,
                                    lastleapinfo.Day,
                                    lastleapinfo.Add ? "add" : "subtract");
                            }
                            else
                            {
                                label19.Text = String.Format("Leap not scheduled");
                            }

                            break;
                        case EpsilonCommandsIndex.Phase_Correction_Read: // Phase Correction
                            numericUpDown5.Value = epsilondevice.Phase_Correction_Value.Phase_Correction_Value;
                            numericUpDown5.Enabled = true;
                            break;
                        case EpsilonCommandsIndex.Local_Time_Read: // Local Time
                            numericUpDown10_Loc_Hours.Enabled = true;
                            numericUpDown11_Loc_Minutes.Enabled = true;
                            numericUpDown10_Loc_Hours.Value = epsilondevice.Local_Time_Offset.Hours_Offset;
                            numericUpDown11_Loc_Minutes.Value = epsilondevice.Local_Time_Offset.Minute_Offset;

                            button15_Set_Local_Time.Enabled = true;
                            button14_Get_Local_Time.BackColor = Color.LightGreen;
                            break;
                        case EpsilonCommandsIndex.GPS_Init_Read: // GPS Positioning Init
                            button9_Read_GPS.BackColor = Color.LightGreen;

                            switch (epsilondevice.GPS_Info.Positioning_Mode)
                            {
                                case EpsilonGPSInformation.GPSPositioningModes.Positioning_Mode_Automatic:
                                    comboBox2.SelectedIndex = 0;
                                    break;
                                case EpsilonGPSInformation.GPSPositioningModes.Positioning_Mode_Mobile:
                                    comboBox2.SelectedIndex = 1;
                                    break;
                                case EpsilonGPSInformation.GPSPositioningModes.Positioning_Mode_Manual:
                                    comboBox2.SelectedIndex = 2;
                                    break;
                            }

                            
                            comboBox2.Enabled = true;

                            if (epsilondevice.GPS_Info.Positioning_Mode == EpsilonGPSInformation.GPSPositioningModes.Positioning_Mode_Manual)
                            {
                                numericUpDown2.Enabled = true;
                                numericUpDown3.Enabled = true;
                                numericUpDown4.Enabled = true;
                            }
                            else
                            {
                                numericUpDown2.Enabled = false;
                                numericUpDown3.Enabled = false;
                                numericUpDown4.Enabled = false;
                            }


                            numericUpDown2.Value = (decimal)epsilondevice.GPS_Info.GPS_Position.Latitude;

                            numericUpDown3.Value = (decimal)epsilondevice.GPS_Info.GPS_Position.Longitude;

                            numericUpDown4.Value = (decimal)epsilondevice.GPS_Info.GPS_Position.Altitude;

                            comboBox3.SelectedIndex = (byte)epsilondevice.GPS_Info.Time_Reference;
                            comboBox3.Enabled = true;
                            button8_Set_GPS.Enabled = true;
                            break;
                        case EpsilonCommandsIndex.TOD_Period_Read: // TOD interval
                            
                            numericUpDown1.Value = epsilondevice.TOD_Output_Period.TOD_Output_Period;
                            numericUpDown1.Enabled = true;

                            button4_Get_TOD_Int.BackColor = Color.LightGreen;
                            button3_Set_TOD_Interval.Enabled = true;
                            break;
                        case EpsilonCommandsIndex.TOD_Setup_Read: // TOD Output Setup
                            comboBox1.SelectedIndex = epsilondevice.TOD_Setup.TODSetup == EpsilonTODSetup.EpsilonTimeOfDayTypes.TOD_Message_Output ?
                                0 : 1;
                            comboBox1.Enabled = true;

                            button2_Get_TOD.BackColor = Color.LightGreen;
                            button1_Set_TOD_Output.Enabled = true;

                            break;
                        case EpsilonCommandsIndex.Manual_Frequency_Read:
                            numericUpDown9.Value = epsilondevice.ManualFreqCorrection.FreqCorrectionValue;
                            numericUpDown9.Enabled = true;
                            button4_Set_Freq_Manual.Enabled = true;
                            button5_Get_Freq_Manual.BackColor = Color.LightGreen;
                            break;
                        case EpsilonCommandsIndex.Status_Read: // status output
                            
                            Update_Status(epsilondevice.LastStatusMessage);

                            button2_Status_Refresh.BackColor = Color.LightGreen;

                            break;
                        case EpsilonCommandsIndex.TOD_Format_1: // TOD format 1
                        case EpsilonCommandsIndex.TOD_Format_2:
                        case EpsilonCommandsIndex.TOD_Format_3:
                        case EpsilonCommandsIndex.TOD_Format_4:
                        case EpsilonCommandsIndex.TOD_Format_5:
                            label5_Status_time.Text = epsilondevice.TOD_Information.GetCurrentTODString;
                            label4.Text = epsilondevice.TOD_Information.GetCurrentFriendlyString;
                            label4.ForeColor = Color.Black;
                            label5_Status_time.ForeColor = Color.Black;
                            timer1_Time_Timeout.Stop();
                            timer1_Time_Timeout.Start();

                            break;
                        case EpsilonCommandsIndex.Error_Code: // invalid command error
                            string errortype = "";
                            switch (currentmessage.Payload[1])
                            {
                                case 0:
                                    errortype = "Incorrect number of bytes";
                                    break;
                                case 1:
                                    errortype = "Unknown message ID";
                                    break;
                                case 2:
                                    errortype = "Unauthorized parameter in DATA section";
                                    break;
                                case 3:
                                    errortype = "Command not valid";
                                    break;
                                case 4:
                                    errortype = "Remote command not authorized";
                                    break;
                                default:
                                    errortype = String.Format("Unknown Error Code, payload is: {0}",
                                        BitConverter.ToString(currentmessage.Payload.ToArray()));
                                    break;

                            }

                            //Epsilon_Command_Info e;

                            if (epsilondevice.Epsilon_Command_List.TryGetValue((int)currentmessage.Payload[0], out e))
                            {
                                sb.AppendFormat("Received NACK for {0} {1} ({3}), the error was: {2}\n", 
                                    e.ReadWrite == EpsilonCommandAccessType.Read ? "Read" : "Write",
                                    e.FriendlyName,
                                    errortype, 
                                    (int)e.Command_Index);
                            }
                            else
                            {
                                sb.AppendFormat("Received NACK for unknown command {0}, the error was: {1}\n", currentmessage.Payload[0], errortype);
                            }
                            break;
                        case EpsilonCommandsIndex.DECODE_FAILED:
                            sb.AppendFormat("Decode failed for {0} {1} (command may not be supported)\n",
                                    e.ReadWrite == EpsilonCommandAccessType.Read ? "Read" : "Write",
                                    e.FriendlyName);
                                    //(int)e.Command_Index);
                            break;
                        default: // unknown handler message
                            //Epsilon_Command_Info e2;

                            if (epsilondevice.Epsilon_Command_List.TryGetValue((int)currentmessage.MessageID, out e))
                            {

                                sb.AppendFormat("Received unhandled message \"{5}\" register \"{0}\", ID {4}, length {1}, payload: {2}, checksum valid: {3}\n",
                                    e.FriendlyName, 
                                    currentmessage.PayloadLength,
                                    BitConverter.ToString(currentmessage.Payload.ToArray()),
                                    checksumvalid.ToString(),
                                    (byte)e.Command_Index,
                                    ((e.ReadWrite == EpsilonCommandAccessType.Read) ? "Read" : "Write"));
                            }
                            else
                            {
                                sb.AppendFormat("Received unknown message id {0}, length {1}, payload: {2}, checksum: {3}\n",
                                    currentmessage.MessageID.ToString(), 
                                    currentmessage.PayloadLength,
                                    BitConverter.ToString(currentmessage.Payload.ToArray()),
                                    checksumvalid.ToString());
                            }

                            break;
                    }
                    receiverstate = serialRXState.serialRXState_Idle;
                }



                


                //sb.Append(Convert.ToByte(currentchar).ToString("x2"));
            }

            richTextBox1_Serial_Log.AppendText(sb.ToString());
            
        }

        /*
         * 
         */
        private void serialData_Process_Tx(EpsilonSerialMessage message)
        {

            if (!serialPort1.IsOpen)
            {
                if (!Try_Open_Serial_Port())
                {
                    return;
                }
            }
            List<byte> txdata = new List<byte>(message.PayloadLength + 10);

            byte checksum = 0;

            txdata.Add(2);

            if (message.MessageID == 2 || message.MessageID == 3 || message.MessageID == 16)
            {
                //checksum ^= 16;
                txdata.Add(16);
            }
            checksum ^= message.MessageID;
            txdata.Add(message.MessageID);

            if (message.PayloadLength == 2 || message.PayloadLength == 3 || message.PayloadLength == 16)
            {
                //checksum ^= 16;
                txdata.Add(16);
            }
            checksum ^= message.PayloadLength;
            txdata.Add(message.PayloadLength);

            foreach(byte currentbyte in message.Payload)
            {
                // escape
                if (currentbyte == 2 || currentbyte == 3 || currentbyte == 16)
                {
                    //checksum ^= 16;
                    txdata.Add(16);
                }
                checksum ^= currentbyte;
                txdata.Add(currentbyte);
            }

            if (checksum == 2 || checksum == 3 || checksum == 16)
            {
                //checksum ^= 16;
                txdata.Add(16);
            }

            txdata.Add(checksum);
            txdata.Add(3);

            serialPort1.Write(txdata.ToArray(), 0, txdata.Count); 

        }

        private void serialPort1_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            SerialPort sp = (SerialPort)sender;
            int size = sp.BytesToRead;
            byte[] rxdata = new byte[size];
            sp.Read(rxdata, 0, size);
            serialData_input(rxdata);
            //backgroundWorker1.RunWorkerAsync(rxdata);
        }

        private void richTextBox1_Serial_Log_TextChanged(object sender, EventArgs e)
        {
            richTextBox1_Serial_Log.SelectionStart = richTextBox1_Serial_Log.Text.Length;
            // scroll it automatically
            richTextBox1_Serial_Log.ScrollToCaret();
        }

        private void button2_Status_Refresh_Click(object sender, EventArgs e)
        {
            Button b = (Button)sender;
            b.BackColor = Color.Orange;
            if (!epsilondevice.GPS_Info.DataValid)
            {
                Epsilon_Issue_Command(EpsilonCommandsIndex.GPS_Init_Read);
                Epsilon_Issue_Command(EpsilonCommandsIndex.Leap_Second_Read);
                Epsilon_Issue_Command(EpsilonCommandsIndex.Phase_Correction_Read);
            }

            if (!epsilondevice.LastVersionMessage.DataValid)
            {
                Epsilon_Issue_Command(EpsilonCommandsIndex.Version_Read);
            }

            Epsilon_Issue_Command(EpsilonCommandsIndex.Status_Read);
        }
		
		





        private void checkBox1_Status_autorefresh_CheckedChanged(object sender, EventArgs e)
        {
            CheckBox box = (CheckBox)sender;
            if (box.Checked)
            {
                timer1_Status_Auto.Start();
                if (!epsilondevice.GPS_Info.DataValid)
                {
                    Epsilon_Issue_Command(EpsilonCommandsIndex.GPS_Init_Read);
                    Epsilon_Issue_Command(EpsilonCommandsIndex.Leap_Second_Read);
                    Epsilon_Issue_Command(EpsilonCommandsIndex.Phase_Correction_Read);
                }

                if (!epsilondevice.LastVersionMessage.DataValid)
                {
                    Epsilon_Issue_Command(EpsilonCommandsIndex.Version_Read);
                }
                Epsilon_Issue_Command(EpsilonCommandsIndex.Status_Read);
            }
            else
            {
                timer1_Status_Auto.Stop();
            }
        }

        private void timer1_Status_Auto_Tick(object sender, EventArgs e)
        {
            button2_Status_Refresh.BackColor = Color.Orange;
            Epsilon_Issue_Command(EpsilonCommandsIndex.Status_Read);
        }

        private void button25_Read_All_Click(object sender, EventArgs e)
        {
            Button b = (Button)sender;
            //b.BackColor = Color.Orange;

            tabControl1.SuspendLayout();
            // we have to step through the tabcontrol since we can't click a button that isn't on screen
            int oldindex = tabControl1.SelectedIndex;

            tabControl1.SelectTab(0);

            button2_Version_read.PerformClick();
            button2_Status_Refresh.PerformClick();

            tabControl1.SelectTab(1);
            button11_Get_Display.PerformClick();
            button13_Get_Alarm.PerformClick();
            button14_Get_Local_Time.PerformClick();
            button19_Get_Force_Holdover.PerformClick();
            button20_Get_Manual_Time.PerformClick();
            button24_Get_1PPS_Phase.PerformClick();
            button2_Get_TOD.PerformClick();
            button4_Get_Step_Seconds.PerformClick();
            button4_Get_TOD_Int.PerformClick();
            button9_Read_GPS.PerformClick();
            button6__Get_Remote.PerformClick();
            button1.PerformClick();
            button5_Get_Freq_Manual.PerformClick();

            // put tabcontrol back to the old tab
            tabControl1.SelectTab(oldindex);
            tabControl1.ResumeLayout();

            // push all the read buttons
            /*
            ReadCommand(Epsilon_Commands_Index.Status_Read);
            ReadCommand(Epsilon_Commands_Index.Version_Read);
            ReadCommand(Epsilon_Commands_Index.TOD_Setup_Read);
            ReadCommand(Epsilon_Commands_Index.TOD_Period_Read);
            ReadCommand(Epsilon_Commands_Index.GPS_Init_Read);
            ReadCommand(Epsilon_Commands_Index.Leap_Second_Read);
            ReadCommand(Epsilon_Commands_Index.Display_Read);
            ReadCommand(Epsilon_Commands_Index.Alarm_Limits_Read);
            ReadCommand(Epsilon_Commands_Index.Local_Time_Read);
            ReadCommand(Epsilon_Commands_Index.Force_Holdover_Read);
            ReadCommand(Epsilon_Commands_Index.Manual_Time_Read);
            ReadCommand(Epsilon_Commands_Index.Remote_Read);
            ReadCommand(Epsilon_Commands_Index.Phase_Correction_Read);*/
        }

        private void timer1_Time_Timeout_Tick(object sender, EventArgs e)
        {
            label4.ForeColor = Color.LightGray;
            label5_Status_time.ForeColor = Color.LightGray;
        }

        private void button2_Version_read_Click(object sender, EventArgs e)
        {
            Button b = (Button)sender;
            b.BackColor = Color.Orange;
            Epsilon_Issue_Command(EpsilonCommandsIndex.Version_Read);
        }

        private void button2_Click(object sender, EventArgs e)
        {
            Button b = (Button)sender;
            b.BackColor = Color.Orange;

            Epsilon_Issue_Command(EpsilonCommandsIndex.TOD_Setup_Read);
        }

        private void button4_Click(object sender, EventArgs e)
        {
            Button b = (Button)sender;
            b.BackColor = Color.Orange;
            Epsilon_Issue_Command(EpsilonCommandsIndex.TOD_Period_Read);
        }

        private void button9_Click(object sender, EventArgs e)
        {
            Button b = (Button)sender;
            b.BackColor = Color.Orange;


            Epsilon_Issue_Command(EpsilonCommandsIndex.GPS_Init_Read);
            //System.Threading.Thread.Sleep(100);
            Epsilon_Issue_Command(EpsilonCommandsIndex.Leap_Second_Read);
            //System.Threading.Thread.Sleep(100);
            Epsilon_Issue_Command(EpsilonCommandsIndex.Phase_Correction_Read);
        }

        private void button11_Click(object sender, EventArgs e)
        {
            Button b = (Button)sender;
            b.BackColor = Color.Orange;
            Epsilon_Issue_Command(EpsilonCommandsIndex.Display_Read);
        }

        private void button13_Click(object sender, EventArgs e)
        {
            Button b = (Button)sender;
            b.BackColor = Color.Orange;
            //Epsilon_Issue_Command(Epsilon_Commands_Index.Version_Read);
            Epsilon_Issue_Command(EpsilonCommandsIndex.Alarm_Limits_Read);
        }

        private void button14_Click(object sender, EventArgs e)
        {
            Button b = (Button)sender;
            b.BackColor = Color.Orange;
            Epsilon_Issue_Command(EpsilonCommandsIndex.Local_Time_Read);
        }

        private void button19_Click(object sender, EventArgs e)
        {
            Button b = (Button)sender;
            b.BackColor = Color.Orange;
            Epsilon_Issue_Command(EpsilonCommandsIndex.Force_Holdover_Read);
        }

        private void button20_Click(object sender, EventArgs e)
        {
            Button b = (Button)sender;
            b.BackColor = Color.Orange;
            Epsilon_Issue_Command(EpsilonCommandsIndex.Manual_Time_Read);
        }

        private void button22_Click(object sender, EventArgs e)
        {

        }

        private void button24_Click(object sender, EventArgs e)
        {
            Button b = (Button)sender;
            b.BackColor = Color.Orange;
            Epsilon_Issue_Command(EpsilonCommandsIndex.Manual_1PPS_Read);
        }

        private void button7_Click(object sender, EventArgs e)
        {
            // issue reset command

            List<byte> payload = new List<byte>(0);

            //payload.Add(checkBox1.Checked ? (byte)0 : (byte)1);

            Epsilon_Issue_Command(EpsilonCommandsIndex.Reset_Clock_Write, payload);
        }

        private void button6_Click(object sender, EventArgs e)
        {
            Button b = (Button)sender;
            b.BackColor = Color.Orange;
            Epsilon_Issue_Command(EpsilonCommandsIndex.Remote_Control_Read);
        }

        private void button4_Click_1(object sender, EventArgs e)
        {
            Button b = (Button)sender;
            b.BackColor = Color.Orange;
            Epsilon_Issue_Command(EpsilonCommandsIndex.Manual_Second_Read);
        }

        private void linkLabel1_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            System.Diagnostics.Process.Start("http://longview.be");
        }

        // exit handler
        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {

            if (serialPort1.IsOpen)
            {
                serialPort1.Close();
            }

            if (comboBox1_Serial_Port.Text != String.Empty)
            {
                Properties.Settings.Default["Selected_COM_Port"] = comboBox1_Serial_Port.Text;
            }
            Properties.Settings.Default.Save();
        }

        private byte[] SplitUInt32_to_byte(UInt32 input)
        {
            List<byte> output = new List<byte>(4);

            output.Add((byte)(input >> 24));
            output.Add((byte)(input >> 16));
            output.Add((byte)(input >> 8));
            output.Add((byte)(input & 0xff));

            return output.ToArray();
        }

        private void button3_Click(object sender, EventArgs e)
        {
            Button b = (Button)sender;
            b.BackColor = Color.Orange;

            EpsilonTODOutputPeriod tod = new EpsilonTODOutputPeriod();

            tod.TOD_Output_Period = (UInt32)numericUpDown1.Value;

            tod.Serialize(out List<byte> payload);

            Epsilon_Issue_Command(EpsilonCommandsIndex.TOD_Period_Write, payload);
        }

        private void button10_Click(object sender, EventArgs e)
        {

            Button b = (Button)sender;
            b.BackColor = Color.Orange;


            

            EpsilonDisplay display = new EpsilonDisplay();

            if (radioButton1.Checked)
            {
                display.GetDisplayMode = EpsilonDisplay.DisplayMode.TOD_Format_1;
            }
            else if (radioButton2.Checked)
            {
                display.GetDisplayMode = EpsilonDisplay.DisplayMode.TOD_Format_2;
            }
            else if (radioButton3.Checked)
            {
                display.GetDisplayMode = EpsilonDisplay.DisplayMode.TOD_Format_3;
            }
            else if (radioButton4.Checked)
            {
                display.GetDisplayMode = EpsilonDisplay.DisplayMode.TOD_Format_4;
            }
            else if (radioButton5.Checked)
            {
                display.GetDisplayMode = EpsilonDisplay.DisplayMode.TOD_Format_5;
            }

            //payload.Add(value);
            //payload.Add(0);

            display.Serialize(out List<byte> payload);


            Epsilon_Issue_Command(EpsilonCommandsIndex.Display_Write, payload);
        }

        private void button12_Click(object sender, EventArgs e)
        {

            Button b = (Button)sender;
            b.BackColor = Color.Orange;

            /*
            List<byte> payload = new List<byte>(10);

            // phase error limit
            payload.AddRange(
                SplitUInt32_to_byte(
                    Convert.ToUInt32(numericUpDown6.Value)));
            // frequency error limit

            // if we set a decimal place when getting, we can use precision settings
            if (numericUpDown7.DecimalPlaces == 1)
            {
                payload.AddRange(
                    SplitUInt32_to_byte(
                        Convert.ToUInt32(numericUpDown7.Value * 10)));
                payload[4] |= 0b10000000;
            }
            else
            {
                payload.AddRange(
                    SplitUInt32_to_byte(
                        Convert.ToUInt32(numericUpDown7.Value)));
            }
            // frequency squelch
            payload.Add(checkBox3.Checked ? (byte)0 : (byte)1);

            // reserved byte
            payload.Add(0);*/

            EpsilonAlarmInformation alarm = new EpsilonAlarmInformation();

            alarm.AlarmAccuracy = numericUpDown7.DecimalPlaces == 1 ?
                EpsilonAlarmInformation.EpsilonAlarmAccuracy.AlarmResolution0_1ppb : EpsilonAlarmInformation.EpsilonAlarmAccuracy.AlarmResolution1ppb;

            alarm.PhaseAlarmLimit = (UInt32)numericUpDown6.Value;

            alarm.FrequencyAlarmLimit = numericUpDown7.Value;

            alarm.FrequencyOutputSquelch = checkBox3.Checked;

            alarm.Serialize(out List<byte> payload);

            Epsilon_Issue_Command(EpsilonCommandsIndex.Alarm_Limit_Write, payload);

        }

        private void button5_Click(object sender, EventArgs e)
        {
            Button b = (Button)sender;
            b.BackColor = Color.Orange;

            // remote enable

            EpsilonRemoteControl rem = new EpsilonRemoteControl();

            rem.RemoteAllowed = checkBox1.Checked;

            rem.Serialize(out List<byte> payload);

            Epsilon_Issue_Command(EpsilonCommandsIndex.Remote_Control_Write, payload);

        }

        private void button8_Click(object sender, EventArgs e)
        {
            // set both GPS info and phase correction

            if (!comboBox2.Enabled)
            {
                return;
            }


            Button b = (Button)sender;
            b.BackColor = Color.Orange;

            EpsilonGPSInformation gpsinfo = new EpsilonGPSInformation();


            switch (comboBox2.SelectedIndex)
            {
                case 0:
                    gpsinfo.Positioning_Mode = EpsilonGPSInformation.GPSPositioningModes.Positioning_Mode_Automatic;
                    //comboBox2.SelectedIndex = 0;
                    break;
                case 1:
                    gpsinfo.Positioning_Mode = EpsilonGPSInformation.GPSPositioningModes.Positioning_Mode_Mobile;
                    //comboBox2.SelectedIndex = 1;
                    break;
                case 2:
                    gpsinfo.Positioning_Mode = EpsilonGPSInformation.GPSPositioningModes.Positioning_Mode_Manual;
                    //comboBox2.SelectedIndex = 2;
                    break;
            }

            gpsinfo.GPS_Position = new GeoCoordinate((double)numericUpDown2.Value, (double)numericUpDown3.Value, (double)numericUpDown4.Value);

            switch (comboBox3.SelectedIndex)
            {
                case 0:
                    gpsinfo.Time_Reference = EpsilonGPSInformation.GPSTimeReferenceModes.Time_Mode_GPS;
                    break;
                case 1:
                    gpsinfo.Time_Reference = EpsilonGPSInformation.GPSTimeReferenceModes.Time_Mode_UTC;
                    break;
            }


            gpsinfo.Serialize(out List<byte> payload);
            
            Epsilon_Issue_Command(EpsilonCommandsIndex.GPS_Init_Write, payload);

            // write phase correction
            EpsilonPhaseCorrection phase = new EpsilonPhaseCorrection();

            phase.Phase_Correction_Value = (UInt32)numericUpDown5.Value;

            phase.Serialize(out payload);

            Epsilon_Issue_Command(EpsilonCommandsIndex.Phase_Correction_Write, payload);

        }

        private void button1_Click(object sender, EventArgs e)
        {
            Button b = (Button)sender;
            b.BackColor = Color.Orange;

            // write phase correction

            EpsilonTODSetup tod = new EpsilonTODSetup();

            switch (comboBox1.SelectedIndex)
            {
                case 0:
                    tod.TODSetup = EpsilonTODSetup.EpsilonTimeOfDayTypes.TOD_Message_Output;
                    break;
                case 1:
                    tod.TODSetup = EpsilonTODSetup.EpsilonTimeOfDayTypes.TOD_Diagnostic_Output;
                    break;
            }

            tod.Serialize(out List<byte> payload);

            //payload.Add((byte)comboBox1.SelectedIndex);

            Epsilon_Issue_Command(EpsilonCommandsIndex.TOD_Setup_Write, payload);
        }

        private void button15_Click(object sender, EventArgs e)
        {
            Button b = (Button)sender;
            b.BackColor = Color.Orange;

            // write phase correction

            // = new List<byte>(2);

            //payload.Add(unchecked((byte)((sbyte)numericUpDown10_Loc_Hours.Value)));
            //payload.Add(unchecked((byte)((sbyte)numericUpDown11_Loc_Minutes.Value)));

            EpsilonLocalTimeOffset time = new EpsilonLocalTimeOffset();
            time.Hours_Offset = Convert.ToSByte(numericUpDown10_Loc_Hours.Value);
            time.Minute_Offset = Convert.ToSByte(numericUpDown11_Loc_Minutes.Value);

            time.Serialize(out List<byte> payload);

            Epsilon_Issue_Command(EpsilonCommandsIndex.Local_Time_Write, payload);
        }

        private void button18_Click(object sender, EventArgs e)
        {
            // force holdover

            Button b = (Button)sender;
            b.BackColor = Color.Orange;

            // write force holdover

            EpsilonForcedHoldover force = new EpsilonForcedHoldover();

            force.Forced_Holdover = checkBox2.Checked;

            force.Serialize(out List<byte> payload);

            Epsilon_Issue_Command(EpsilonCommandsIndex.Force_Holdover_Write, payload);

        }

        private void button17_Click(object sender, EventArgs e)
        {
            if (!dateTimePicker2.Enabled)
            {
                return;
            }


            Button b = (Button)sender;
            b.BackColor = Color.Orange;




            DateTime time = dateTimePicker2.Value;

            Write_Manual_Date(time);
        }

        private void Write_Manual_Date(DateTime time, EpsilonCommandsIndex ep = EpsilonCommandsIndex.Manual_Time_Write)
        {
            EpsilonManualTOD tod = new EpsilonManualTOD();

            tod.ManualTime = time;

            tod.Serialize(out List<byte> payload);

            Epsilon_Issue_Command(ep, payload);
        }

        private void button2_Click_1(object sender, EventArgs e)
        {
            Button b = (Button)sender;
            b.BackColor = Color.Orange;

            EpsilonManualSecondCorrection second = new EpsilonManualSecondCorrection();

            second.CorrectionType = radioButton7_minus.Checked ?
                EpsilonManualSecondCorrection.EpsilonManualSecondCorrectionTypes.Subtract_1_Second :
                EpsilonManualSecondCorrection.EpsilonManualSecondCorrectionTypes.Add_1_Second;

            // write manual second offset

            //List<byte> payload = new List<byte>(1);
            //payload.Add(radioButton7_minus.Checked ? (byte)1 : (byte)0);

            second.Serialize(out List<byte> payload);

            Epsilon_Issue_Command(EpsilonCommandsIndex.Manual_Second_Write, payload);
        }

        private void button16_Click(object sender, EventArgs e)
        {
            if (!dateTimePicker2.Enabled)
            {
                return;
            }

            dateTimePicker2.Value = DateTime.UtcNow.AddSeconds(1);

            DateTime time = dateTimePicker2.Value;

            Write_Manual_Date(time);
        }

        private void button23_Click(object sender, EventArgs e)
        {
            Button b = (Button)sender;
            b.BackColor = Color.Orange;


            EpsilonManual1PPSPhaseCorrection pps = new EpsilonManual1PPSPhaseCorrection();

            pps.PPSCorrectionValue = numericUpDown8.Value;

            pps.Serialize(out List<byte> payload);

            Epsilon_Issue_Command(EpsilonCommandsIndex.Manual_1PPS_Write, payload);
        }

        private void comboBox2_SelectedIndexChanged(object sender, EventArgs e)
        {
            numericUpDown2.Enabled = false;
            numericUpDown3.Enabled = false;
            numericUpDown4.Enabled = false;

            numericUpDown2.ReadOnly = true;
            numericUpDown3.ReadOnly = true;
            numericUpDown4.ReadOnly = true;
            //comboBox2.Enabled = false;
            switch (comboBox2.SelectedIndex)
            {
                case 0:
                    //epsilondevice.GPS_Info.Positioning_Mode = EpsilonGPSInformation.GPSPositioningModes.Positioning_Mode_Automatic;
                    //comboBox2.SelectedIndex = 0;
                    break;
                case 1:
                    //epsilondevice.GPS_Info.Positioning_Mode = EpsilonGPSInformation.GPSPositioningModes.Positioning_Mode_Mobile;
                    //comboBox2.SelectedIndex = 1;
                    break;
                case 2:
                    numericUpDown2.Enabled = true;
                    numericUpDown3.Enabled = true;
                    numericUpDown4.Enabled = true;
                    numericUpDown2.ReadOnly = false;
                    numericUpDown3.ReadOnly = false;
                    numericUpDown4.ReadOnly = false;
                    //epsilondevice.GPS_Info.Positioning_Mode = EpsilonGPSInformation.GPSPositioningModes.Positioning_Mode_Manual;
                    //comboBox2.SelectedIndex = 2;
                    break;
            }
        }

        private void numericUpDown2_ValueChanged(object sender, EventArgs e)
        {
            //epsilondevice.GPS_Info.GPS_Position.Latitude = (double)numericUpDown2.Value;
        }

        private void numericUpDown3_ValueChanged(object sender, EventArgs e)
        {
            //epsilondevice.GPS_Info.GPS_Position.Longitude = (double)numericUpDown3.Value;
        }

        private void numericUpDown4_ValueChanged(object sender, EventArgs e)
        {
            //epsilondevice.GPS_Info.GPS_Position.Altitude = (double)numericUpDown4.Value;
        }

        private void button1_Click_1(object sender, EventArgs e)
        {
            Button b = (Button)sender;
            b.BackColor = Color.Orange;

            Epsilon_Issue_Command(EpsilonCommandsIndex.GPS_Init_Time_Read);

        }

        private void button3_Click_1(object sender, EventArgs e)
        {
            Button b = (Button)sender;
            b.BackColor = Color.Orange;




            DateTime time = dateTimePicker2.Value;

            Write_Manual_Date(time, EpsilonCommandsIndex.GPS_Init_Time_Write);
        }

        private void button2_Click_2(object sender, EventArgs e)
        {
            Button b = (Button)sender;
            b.BackColor = Color.Orange;




            DateTime time = DateTime.UtcNow.AddSeconds(1);

            Write_Manual_Date(time, EpsilonCommandsIndex.GPS_Init_Time_Write);
        }

        private void button5_Click_1(object sender, EventArgs e)
        {
            Button b = (Button)sender;
            b.BackColor = Color.Orange;

            Epsilon_Issue_Command(EpsilonCommandsIndex.Manual_Frequency_Read);
        }

        private void button4_Click_2(object sender, EventArgs e)
        {
            Button b = (Button)sender;
            b.BackColor = Color.Orange;


            EpsilonManualFrequencyCorrection freq = new EpsilonManualFrequencyCorrection();

            freq.FreqCorrectionValue = numericUpDown9.Value;

            freq.Serialize(out List<byte> payload);

            Epsilon_Issue_Command(EpsilonCommandsIndex.Manual_Frequency_Write, payload);
        }
    }
}
