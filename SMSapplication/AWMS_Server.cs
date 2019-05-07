using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO.Ports;
using System.IO;
using MySql.Data.MySqlClient;
using System.Threading;

namespace SMSapplication
{
    class AWMS_Server
    {
        /**** Database *****/
        private MySqlConnection connection;
        private String connectionString;
        private const String DATABASE = "ryane";
        private const String USER = "root";
        private const String PASSWORD = "";
        private const String HOST = "localhost";

        /**** From Smart Bro *****/
        private string portName = "COM9";
        private int baudRate = 460800;//460800;
        private int dataBits = 8;
        private int read_timeout = 10000;
        private int write_timeout = 10000;
        private Char[] delimiter = new Char[]{ ',', ' ','\t','\n','\r'};

        /**** For Caculation Billing ****/
        private double consumption_rate = 0.1475;
        private String[] months = {"January","February","March","April","May","June","July",
                                  "August","September","October","November","December"};
        private double maintenance_fee = 30.0;
        private double VAT = 0.12;

        SerialPort port = new SerialPort();
        clsSMS objclsSMS = new clsSMS();
        ShortMessageCollection objShortMessageCollection = new ShortMessageCollection();

        public AWMS_Server()
        {
            /***Connect the Database *****/
            this.connectionString = "SERVER = " + HOST + " ; " + "DATABASE = " + DATABASE +
                " ; " + "UID = " + USER + " ; " + "PASSWORD = " + PASSWORD + ";";
        }

        public void connectDatabase()
        {
            try
            {
                this.connection = new MySqlConnection(this.connectionString);
                this.connection.Open();
                Console.WriteLine("Connected to Database.");
            }
            catch (MySqlException ex)
            {
                Console.WriteLine("Error: " + ex.ErrorCode + " | " + ex.Message);
            }
        }

        public void connectPort()
        {
            try
            {
                Console.WriteLine("Enter Modem's COM Port:");
                this.portName = Console.ReadLine();
                //Open communication port 
                this.port = objclsSMS.OpenPort(portName, baudRate, dataBits, read_timeout, write_timeout);
            
                if (this.port != null)
                {
                   
                    //MessageBox.Show("Modem is connected at PORT " + this.cboPortName.Text);
                    Console.WriteLine("Modem is connected at PORT " + portName);
                    Console.WriteLine("Connected at " + portName);
                }

                else
                {
                    //MessageBox.Show("Invalid port settings");
                    Console.WriteLine("Invalid Port Settings");
                }
            }
            catch (Exception ex)
            {
                ErrorLog(ex.Message);
            }
        }

        public void readSMS()
        {
            try
            {
                string strCommand = "AT+CMGL=\"REC UNREAD\"";
                // If SMS exist then read SMS
                #region Read SMS
                //.............................................. Read all SMS ....................................................
                objShortMessageCollection = objclsSMS.ReadSMS(this.port, strCommand);
                //Console.WriteLine(objShortMessageCollection.Count);
                foreach (ShortMessage msg in objShortMessageCollection)
                {
                    
                   String message = msg.Message;  
                   int customer_id = 0;
                   double reading = 0;
                   double flowrate = 0; 
                   Console.WriteLine("SMS From: " + msg.Sender + ", Message: " + message);                    
                   Console.WriteLine("Inserting to DB: " + msg.Message);
                   String[] tokens = message.Split(delimiter,StringSplitOptions.RemoveEmptyEntries);
                    
                   if (tokens.Length == 3)
                   {
                       customer_id = Int32.Parse(tokens[0]);
                       reading = Double.Parse(tokens[1]);
                       flowrate = Double.Parse(tokens[2]);
                   }
                   foreach (var token in tokens)
                   {
                       Console.WriteLine(token);
                   }
                   MySqlCommand comm = this.connection.CreateCommand();
                   comm.CommandText = "INSERT INTO readings(id,reading,flowrate,datetime) SELECT c.id,@reading,@flowrate,NOW() FROM customers c WHERE c.custID = @custID";
                   comm.Parameters.AddWithValue("@custID", customer_id);
                   comm.Parameters.AddWithValue("@reading", reading);
                   comm.Parameters.AddWithValue("@flowrate", flowrate);
                   comm.ExecuteNonQuery();                   
                }
                #endregion
            }catch(Exception ex){
                Console.WriteLine(ex.Message);
            }
        }

        public void notifyClient()
        {
            //.............................................. Send SMS ....................................................
            MySqlCommand comm = null;
            MySqlDataReader rdr = null;
            #region Send SMS - Notify Client
            int i = 0;
            try
            {
                comm = this.connection.CreateCommand();
                comm.CommandText = "SELECT c.custID,c.lname, c.fname, c.phone, c.selected_month, c.selected_year, (SELECT SUM(r.reading) " +
                            "FROM readings r WHERE r.id = c.id AND MONTH(r.datetime) = c.selected_month " +
                            "AND YEAR(r.datetime) = c.selected_year GROUP BY r.id) AS " +
                            "total_readings FROM customers c WHERE c.send_notification = 1";
                
                rdr = comm.ExecuteReader();

                String message = "";
                String contact_number = "";
                String name = "";
                double total_readings = 0;
                double total_billings = 0;
                double total_due = 0;
                String selected_date = "";
                List<String> custIDs = new List<String>();

                
                while (rdr.Read())
                {
                    custIDs.Add(rdr["custID"].ToString());
                    name = rdr["lname"] + ", " + rdr["fname"];
                    contact_number = (String)rdr["phone"];
                    Console.WriteLine(contact_number);
                    total_readings = 0;
                    if(rdr["total_readings"] != System.DBNull.Value){
                        total_readings = Double.Parse(rdr["total_readings"].ToString());
                    }
                    total_billings = consumption_rate * total_readings;
                    total_due = total_billings + (total_billings * VAT) + maintenance_fee;
                    selected_date = months[Int32.Parse(rdr["selected_month"].ToString())] + ", " + rdr["selected_year"];
                    message = ("Client: " + custIDs.ElementAt(i) + ", " + name + ". You have a total consumption of " + total_readings
                        + " Gallons, Billing Amount of Php " + total_billings + " on " + selected_date);
                    
                    Console.WriteLine(message);
                    if (objclsSMS.sendMsg(this.port, contact_number, message))
                    {
                        //MessageBox.Show("Message has sent successfully");
                        Console.WriteLine("Message has sent successfully. Notifications has been sent to customer id: " + custIDs);
                    }
                    else
                    {
                        //MessageBox.Show("Failed to send message");
                        Console.WriteLine("Failed to send message");
                    }
                    i++;
                }
                rdr.Close();
                for (int j = 0; j < i; j++)
                {
                    MySqlCommand commUpdate = this.connection.CreateCommand();
                    commUpdate.CommandText = "UPDATE customers SET send_notification = 0 WHERE custID = @custID";
                    commUpdate.Parameters.AddWithValue("@custID", custIDs.ElementAt(j));
                    commUpdate.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                for (int j = 0; j < i; j++)
                {
                    MySqlCommand commUpdate = this.connection.CreateCommand();
                    commUpdate.CommandText = "UPDATE customers SET send_notification = 0 WHERE custID = @custID";
                    commUpdate.Parameters.AddWithValue("@custID", custIDs.ElementAt(j));
                    commUpdate.ExecuteNonQuery();
                }
                Console.WriteLine(ex.Message);
            }
            finally
            {
                rdr.Close();
            }
            #endregion
        }

        public void updateValve()
        {
            //.............................................. Send SMS ....................................................
            MySqlCommand comm = null;
            MySqlDataReader rdr = null;
            #region Send SMS - Update Valve
            try
            {
                comm = this.connection.CreateCommand();
                comm.CommandText = "SELECT * FROM customers WHERE change_valve = 1";
                rdr = comm.ExecuteReader();

                String valve = "";
                String contact_number = "";
                String name = "";
                
                List<String> custIDs = new List<String>();

                int i = 0;
                while (rdr.Read())
                {
                    custIDs.Add(rdr["custID"].ToString());
                    name = rdr["lname"] + ", " + rdr["fname"];
                    contact_number = rdr["hardware_gsm"].ToString();
                    if (Int32.Parse(rdr["valve"].ToString())==1)
                    {
                        valve = ("ON");
                    }else{
                        valve = ("OFF");
                    }
                    Console.WriteLine(valve);
                    
                    if (objclsSMS.sendMsg(this.port, contact_number, valve))
                    {
                        //MessageBox.Show("Message has sent successfully");
                        Console.WriteLine("Message has sent successfully. Updating Valve for customer id: " + custIDs);
                    }
                    else
                    {
                        //MessageBox.Show("Failed to send message");
                        Console.WriteLine("Failed to send message");
                    }
                    i++;
                }
                rdr.Close();
                for (int j = 0; j < i; j++)
                {
                    MySqlCommand commUpdate = this.connection.CreateCommand();
                    commUpdate.CommandText = "UPDATE customers SET change_valve = 0 WHERE custID = @custID";
                    commUpdate.Parameters.AddWithValue("@custID", custIDs.ElementAt(j));
                    commUpdate.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
            finally
            {
                rdr.Close();
            }
            #endregion
        }

        public void runSystem()
        {
            while (true)
            {
                this.readSMS();
                //Console.WriteLine("Scanning new message");
                Console.WriteLine("Waiting for Sender...");
                this.notifyClient();
                this.updateValve();
                //Console.WriteLine("Send Notification to User");
                Thread.Sleep(800);
            }
        }


        #region Error Log
        public void ErrorLog(string Message)
        {
            StreamWriter sw = null;

            try
            {
                
                string sLogFormat = DateTime.Now.ToShortDateString().ToString() + " " + DateTime.Now.ToLongTimeString().ToString() + " ==> ";
                //string sPathName = @"E:\";
                string sPathName = @"SMSapplicationErrorLog_";

                string sYear = DateTime.Now.Year.ToString();
                string sMonth = DateTime.Now.Month.ToString();
                string sDay = DateTime.Now.Day.ToString();

                string sErrorTime = sDay + "-" + sMonth + "-" + sYear;

                sw = new StreamWriter(sPathName + sErrorTime + ".txt", true);

                sw.WriteLine(sLogFormat + Message);
                sw.Flush();

            }
            catch (Exception ex)
            {
                //ErrorLog(ex.ToString());
                Console.WriteLine(ex.Message);
            }
            finally
            {
                if (sw != null)
                {
                    sw.Dispose();
                    sw.Close();
                }
            }

        }
        #endregion 

    }
}
