using MathNet.Numerics.Interpolation;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
//using System.Data.Entity;
using System.Drawing.Printing;
using System.IO.Ports;
using System.Net;
using System.Net.NetworkInformation;
using System.Threading;
using System.Timers;
using EasyModbus;
using Microsoft.Win32;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;
using MathNet.Numerics.LinearAlgebra;
using System.Windows.Forms.DataVisualization.Charting;
using System.IO;
using System.Data.SqlClient;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.TaskbarClock;
using System.Collections.Specialized;
using System.Configuration;
using System.Windows.Input;
using System.Diagnostics;
using System.Data.Entity;
using Newtonsoft.Json.Linq;


namespace ERN028_MakinaVeriTopalamaFormsApp
{
    public partial class Form1 : Form
    {
        //private System.Timers.Timer dataTimer;

        private Dictionary<string, MachineInfo> AdressList = new Dictionary<string, MachineInfo>();

        private string ip_adress;

        int[] registers = new int[100];

        int currnetProduction; // Production : DWORD : D200 : 
        int OKProductCount; // OK qty : DWORD : D202 : 202
        int NGProductCount; // NG qty : DWORD : D204 : 204
        float OKRatio; // OK Ratio : DWORD : D208 : 208
        float CycleTime; // Current Beat : DWORD : D206 : 206

        bool auto;
        bool run;
        bool ready;
        bool fault;

        ModbusClient modbusClient;

        bool[] statusRegisters = new bool[100];

        public Form1()
        {
            InitializeComponent();

            ernamas_dijitalEntities ernadb = new ernamas_dijitalEntities();

            ModbusConnection();

            MainLoop();
        }

        public void MainLoop()
        {
            if (modbusClient.Connected)
            {
                while (modbusClient.Connected)
                {
                    ModbusDataRead();

                    VeriTabanıYazma();

                    Thread.Sleep(200); // Çoklu istekler ve yüksek trafik kaynaklı uygulama çökmesine karşı
                }
            }
            else // Bağlantı koparsa
            {
                // Log eklenecek
                ModbusConnection();
                MainLoop();
            }
        }

        public void ModbusConnection()
        {
            try
            {
                string ip_adress = "10.10.16.30";
                int port = 502;
                modbusClient = new ModbusClient(ip_adress, port);
                modbusClient.Connect();

                textBox1.Text = "";
            }
            catch (Exception)
            {

                throw;
            }
        }

        private readonly string logFilePath = "ModbusLog.txt";

        public void ModbusDataRead()
        {
            var stopwatch = new Stopwatch();
            stopwatch.Start();

            using (StreamWriter logWriter = new StreamWriter(logFilePath, true))
            {
                try
                {
                    int startAddress = 200;
                    int numRegisters = 10;

                    //logWriter.WriteLine(); // Bir satır boşluk bırakır
                    //logWriter.WriteLine($"{DateTime.Now}: Modbus Client Connected: {modbusClient.Connected}");
                    registers = modbusClient.ReadHoldingRegisters(startAddress, numRegisters);

                    currnetProduction = registers[0]; // Production : DWORD : D200 : 200
                    OKProductCount = registers[2]; // OK qty : DWORD : D202 : 202
                    NGProductCount = registers[4]; // NG qty : DWORD : D204 : 204
                    OKRatio = registers[8]; // OK Ratio : DWORD : D208 : 208
                    CycleTime = registers[6] / 10; // Current Beat : DWORD : D206 : 206

                    int statusAddress = 30730;
                    int statusNumRegisters = 20;
                    statusRegisters = modbusClient.ReadCoils(statusAddress, statusNumRegisters);

                    run = statusRegisters[10]; // Run : BOOL : B10 : 30740
                    auto = statusRegisters[12]; // Auto/Manuel : BOOL : B12 : 30742 -> (False: Manuel / True: Auto)
                    ready = statusRegisters[8]; // Ready : BOOL : B08 : 30738
                    fault = statusRegisters[15]; // Fault : BOOL : B15 : 30745

                    //logWriter.WriteLine($"{DateTime.Now}: Current Production: {currnetProduction}");
                    //logWriter.WriteLine($"{DateTime.Now}: Cycle Time: {CycleTime}");
                }
                catch (System.IndexOutOfRangeException ex)
                {
                    logWriter.WriteLine($"{DateTime.Now}: Exception: System.IndexOutOfRangeException");
                    logWriter.WriteLine($"{DateTime.Now}: Message: {ex.Message}");
                    logWriter.WriteLine($"{DateTime.Now}: StackTrace: {ex.StackTrace}");

                    ReconnectModbusClient();
                }
                catch (System.IO.IOException ex)
                {
                    logWriter.WriteLine($"{DateTime.Now}: Exception: System.IO.IOException");
                    logWriter.WriteLine($"{DateTime.Now}: Message: {ex.Message}");
                    logWriter.WriteLine($"{DateTime.Now}: StackTrace: {ex.StackTrace}");

                    ReconnectModbusClient();
                }
                catch (Exception e)
                {
                    logWriter.WriteLine($"{DateTime.Now}: General Exception Caught");
                    logWriter.WriteLine($"{DateTime.Now}: Message: {e.Message}");
                    logWriter.WriteLine($"{DateTime.Now}: StackTrace: {e.StackTrace}");
                }
                finally
                {
                    stopwatch.Stop();
                    logWriter.WriteLine($"{DateTime.Now}: Modbus read operation took {stopwatch.ElapsedMilliseconds} ms");
                }
            }
        }


        public void VeriTabanıYazma()
        {
            int lastProduction;
            int lastStatusTypeID;
            int temp_Status = 0;
            using (var context = new ernamas_dijitalEntities())
            {
                ip_adress = "10.10.16.30";
                var _machineID = context.Machines.FirstOrDefault(l => l.IPAdresses == ip_adress);
                var machineStatus = context.MachineCurrentStatus.FirstOrDefault(l => l.MachineID == _machineID.MachineID);
                //var machineChangeLogs = context.MachineChangeLogs.FirstOrDefault(l => l.MachineID == _machineID.MachineID);

                if (machineStatus != null)
                {
                    // Karşılaştırma için bir önceki kayıttaki veriler
                    lastProduction = Convert.ToInt32(machineStatus.ProducedItems);
                    lastStatusTypeID = Convert.ToInt32(machineStatus.StatusTypeID);

                    // Makine artış kontrolü
                    int differenceOfProduction = (currnetProduction - lastProduction);

                    if (differenceOfProduction >= 1) // Eğer artış olmuşsa
                    {
                        // O anki artış ile tablodaki değeri güncelle
                        machineStatus.CurrentDailyProduction = Convert.ToInt32(machineStatus.CurrentDailyProduction) + 1; // Eğer artış varsa bir arttır

                        // Cycle Time tabloya tek tek kayıt
                        SaveNewCycleTimeData(machineStatus.MachineID, DateTime.Now, machineStatus.CurrentDailyProduction, CycleTime);
                    }

                    // Hesaplama yapmak için geçici durum bilgisi atama
                    if (ready)
                    {
                        temp_Status = 1; // Ready
                    }
                    else if (run)
                    {
                        temp_Status = 2; // Run
                    }
                    else if (fault)
                    {
                        temp_Status = 4; // Fault
                    }
                    else
                    {
                        temp_Status = 5; // Invalid
                    }


                    // Kayıt
                    machineStatus.ProducedItems = currnetProduction;
                    machineStatus.CycleTime = CycleTime;
                    machineStatus.OKProductCount = OKProductCount;
                    machineStatus.NGProductCount = NGProductCount;
                    machineStatus.AverageCycleTime = 0;
                    machineStatus.OKRatio = OKRatio;
                    machineStatus.Timestamp = DateTime.Now;
                    machineStatus.StatusTypeID = temp_Status;



                    // Statü değişiminde kontrol
                    if (lastStatusTypeID != temp_Status)
                    {
                        var newMachineChangeLog = new MachineChangeLogs
                        {
                            MachineID = machineStatus.MachineID,
                            Timestamp = DateTime.Now,
                            PreviousValue = lastStatusTypeID,
                            ChangeValue = temp_Status
                        };

                        context.MachineChangeLogs.Add(newMachineChangeLog);

                        // Makine tekrar çalışma durumuna geçtiğince ne zaman durmuştu kontrolü yapan bölüm
                        if (temp_Status == 2 && lastStatusTypeID != 2)
                        {
                            // Makine ID'sine göre tüm kayıtları al ve zamana göre sıralı listeyi oluştur
                            var machineChangeLogs = context.MachineChangeLogs
                                                            .Where(l => l.MachineID == _machineID.MachineID)
                                                            .OrderByDescending(l => l.Timestamp)
                                                            .ToList();

                            // Son kayıttan itibaren önceki kayıtlara bakarak ilerle
                            for (int i = 0; i < machineChangeLogs.Count; i++)
                            {
                                var previousLog = machineChangeLogs[i];

                                // Eğer PreviousValue 2 ise ve ChangeValue 2 değilse, bu kaydı bulduk
                                if (previousLog.ChangeValue != 2 && previousLog.PreviousValue == 2)
                                {
                                    var newMachineDowntimeReport = new MachineDowntimeReports
                                    {
                                        DownTime = previousLog.Timestamp,
                                        DownEndTime = newMachineChangeLog.Timestamp,
                                        MachineID = newMachineChangeLog.MachineID,
                                        // Varsayılan ya da boş bırakabileceğiniz diğer alanlar
                                        MachineFaultCategoriesID = 0, // Default or appropriate value
                                        AdditionalFaultInfo = string.Empty,
                                        MachineSolutionInfoCategoriesID = 0, // Default or appropriate value
                                        AdditionalSolutionInfo = string.Empty,
                                        employeeId = 0 // Default or appropriate value
                                    };

                                    context.MachineDowntimeReports.Add(newMachineDowntimeReport);

                                    break;
                                }
                            }
                        }
                    }


                    //// Komut zaman aşımını 60 saniyeye ayarlar
                    //context.Database.CommandTimeout = 60; // Bazı durumlarda veri tabanımıza veri yazma durumu sıkışıyor

                    context.SaveChanges();
                }
            }
        }

        public void ReconnectModbusClient()
        {
            using (StreamWriter logWriter = new StreamWriter(logFilePath, true))
            { 
                try
                {
                    modbusClient.Disconnect();
                }
                catch (Exception ex)
                {
                    logWriter.WriteLine($"Failed to disconnect: {ex.Message}");
                }

                try
                {
                    modbusClient.Connect();
                    logWriter.WriteLine("Modbus client reconnected.");
                }
                catch (Exception ex)
                {
                    logWriter.WriteLine($"Failed to reconnect: {ex.Message}");
                }
            }
        }

        // MODBUS İLE VERİ TOPLAMA

        private static int intervalBetweenMachines = 800; // milliseconds aralıklarla makinalara veri isteği atıyor

        private int currentMachineIndex = -1;

        private CancellationTokenSource cts = new CancellationTokenSource();

        private void btnReadData_Click_1(object sender, EventArgs e)
        {
            //string temp_ip_for_test = "10.10.16.70";
            //if (PingHost(temp_ip_for_test)) MessageBox.Show($"Ping test is Successful\n for: " + temp_ip_for_test);
            //else MessageBox.Show($"Ping test is Unsuccessful " + temp_ip_for_test);

            StartReadingData();
        }

        private void StartReadingData()
        {
            // Start the timer thread
            Task.Run(() => TimerThread(cts.Token));

            // Start the data reading thread
            Task.Run(() => DataReadingThread(cts.Token));

            // Start the night Schedule thred
            Task.Run(() => ScheduleNightlyTask());
        }

        private async Task TimerThread(CancellationToken token)
        {
            int machineIndex = 0;

            while (!token.IsCancellationRequested)
            {
                if (machineIndex >= AdressList.Count)
                {
                    machineIndex = 0;
                }

                // Signal the data reading thread to read data from the next machine
                currentMachineIndex = machineIndex;
                machineIndex++;
                Invoke((Action)(() =>
                {
                    textBox2.Text = currentMachineIndex.ToString();
                }));

                await Task.Delay(intervalBetweenMachines);
            }
        }

        private async void DataReadingThread(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                if (currentMachineIndex == -1)
                {
                    // No machine to read data from, continue the loop
                    await Task.Delay(10); // 10 milisaniye aralıklarla makine okuma zamanı gelmiş mi kontrol et
                    continue;
                }

                Stopwatch stopwatch = new Stopwatch();
                stopwatch.Start();


                var item = AdressList.ElementAt(currentMachineIndex);
                var ip_adress = item.Value.IPAddress;
                var port = item.Value.Port;
                var ExpectedCycleTime = item.Value.ExpectedCycleTime;

                int temp_Status = 0;
                int lastStatusTypeID = 0;
                try
                {
                    //ip_adress = "10.10.16.30";
                    //port = 502;
                    ModbusClient modbusClient = new ModbusClient(ip_adress, port);
                    modbusClient.ConnectionTimeout = 100;
                    modbusClient.Connect();

                    await Task.Delay(50);

                    int startAddress = 200;
                    int numRegisters = 10;
                    int[] registers = modbusClient.ReadHoldingRegisters(startAddress, numRegisters);

                    int currnetProduction = registers[0]; // Production : DWORD : D200 : 200
                    int OKProductCount = registers[2]; // OK qty : DWORD : D202 : 202
                    int NGProductCount = registers[4]; // NG qty : DWORD : D204 : 204
                    float OKRatio = registers[8]; // OK Ratio : DWORD : D208 : 208
                    float CycleTime = registers[6] / 10; // Current Beat : DWORD : D206 : 206

                    int statusAddress = 30730;
                    int statusNumRegisters = 20;
                    bool[] statusRegisters = modbusClient.ReadCoils(statusAddress, statusNumRegisters);

                    bool run = statusRegisters[10]; // Run : BOOL : B10 : 30740
                    //bool auto = statusRegisters[12]; // Auto/Manuel : BOOL : B12 : 30742 -> (False: Manuel / True: Auto)
                    bool ready = statusRegisters[8]; // Ready : BOOL : B08 : 30738
                    bool fault = statusRegisters[15]; // Fault : BOOL : B15 : 30745

                    await Task.Delay(50);

                    modbusClient.Disconnect();

                    // Önceki veriler ile kontrol

                    int lastProduction;
                    //double lastCyceTime;
                    //int lastOKProductCount;
                    //int lastNGProductCount;
                    //int lastAverageCycleTime;
                    //int lastOKRatio;
                    //int lastStatusTypeID; // Yukarı taşındı catch içinde kullanmak için


                    // Registerlardan hatalı sıfır gelme sorunu

                    // Check if all elements in the registers array are zero
                    bool areAllRegistersZero = registers.All(r => r == 0); // Eğer hepsi 0 ise areAllRegistersZero -> True olur

                    // Check if all elements in the statusRegisters array are false
                    bool areAllStatusRegistersFalse = statusRegisters.All(sr => sr == false); // Eğer hepsi false ise areAllStatusRegistersFalse -> True olur

                    if (areAllRegistersZero && areAllStatusRegistersFalse)
                    {
                        Debug.WriteLine(" All Register are Zero ");
                    }

                    if (!areAllRegistersZero && !areAllStatusRegistersFalse)
                    {
                        using (var context = new ernamas_dijitalEntities())
                        {
                            var _machineID = context.Machines.FirstOrDefault(l => l.IPAdresses == ip_adress);
                            var machineStatus = context.MachineCurrentStatus.FirstOrDefault(l => l.MachineID == _machineID.MachineID);
                            //var machineChangeLogs = context.MachineChangeLogs.FirstOrDefault(l => l.MachineID == _machineID.MachineID);

                            if (machineStatus != null)
                            {
                                lastProduction = Convert.ToInt32(machineStatus.ProducedItems);
                                //lastCyceTime = Convert.ToInt32(machineStatus.CycleTime);
                                //lastOKProductCount = Convert.ToInt32(machineStatus.OKProductCount);
                                //lastNGProductCount = Convert.ToInt32(machineStatus.NGProductCount);
                                //lastAverageCycleTime = Convert.ToInt32(machineStatus.AverageCycleTime);
                                //lastOKRatio = Convert.ToInt32(machineStatus.OKRatio);
                                lastStatusTypeID = Convert.ToInt32(machineStatus.StatusTypeID);

                                // Makine artış kontrolü
                                int differenceOfProduction = (currnetProduction - lastProduction);

                                if (differenceOfProduction >= 1) // Eğer artış olmuşsa
                                {
                                    // O anki artış ile tablodaki değeri güncelle
                                    machineStatus.CurrentDailyProduction = Convert.ToInt32(machineStatus.CurrentDailyProduction) + differenceOfProduction;

                                    // Cycle Time tabloya tek tek kayıt
                                    SaveNewCycleTimeData(machineStatus.MachineID, DateTime.Now, machineStatus.CurrentDailyProduction, CycleTime);
                                }

                                // Kayıt
                                machineStatus.ProducedItems = currnetProduction;
                                machineStatus.CycleTime = CycleTime;
                                machineStatus.OKProductCount = OKProductCount;
                                machineStatus.NGProductCount = NGProductCount;
                                machineStatus.AverageCycleTime = 0;
                                machineStatus.OKRatio = OKRatio;
                                machineStatus.Timestamp = DateTime.Now;

                                // Hesaplama yapmak için geçici durum bilgisi
                                if (run) temp_Status = 2;
                                else if (ready) temp_Status = 1;
                                else if (fault) temp_Status = 4;

                                machineStatus.StatusTypeID = temp_Status;

                                // Statü değişiminde kontrol
                                Debug.WriteLine("Önceki Durum: ", lastStatusTypeID, "Sonraki Durum: ", temp_Status);
                                if (lastStatusTypeID != temp_Status)
                                {
                                    var newMachineChangeLog = new MachineChangeLogs
                                    {
                                        MachineID = machineStatus.MachineID,
                                        Timestamp = DateTime.Now,
                                        PreviousValue = lastStatusTypeID,
                                        ChangeValue = temp_Status
                                    };

                                    context.MachineChangeLogs.Add(newMachineChangeLog);
                                }


                                context.SaveChanges();
                            }
                        }
                    }
                }
                catch (Exception)
                {
                    using (var context = new ernamas_dijitalEntities())
                    {
                        var _machineID = context.Machines.FirstOrDefault(l => l.IPAdresses == ip_adress);
                        var machineStatus = context.MachineCurrentStatus.FirstOrDefault(l => l.MachineID == _machineID.MachineID);

                        if (machineStatus != null)
                        {
                            int temp_StatusType = Convert.ToInt32(machineStatus.StatusTypeID);
                            lastStatusTypeID = temp_StatusType;

                            machineStatus.StatusTypeID = 3;
                            machineStatus.Timestamp = DateTime.Now;

                            // Statü değişiminde kontrol
                            Debug.WriteLine($"Önceki Durum: {lastStatusTypeID}, Sonraki Durum: {machineStatus.StatusTypeID}");

                            if (lastStatusTypeID != machineStatus.StatusTypeID)
                            {
                                var newMachineChangeLog = new MachineChangeLogs
                                {
                                    MachineID = machineStatus.MachineID,
                                    Timestamp = DateTime.Now,
                                    PreviousValue = lastStatusTypeID,
                                    ChangeValue = machineStatus.StatusTypeID
                                };

                                context.MachineChangeLogs.Add(newMachineChangeLog);
                            }

                            context.SaveChanges();
                        }
                    }
                }


                stopwatch.Stop();
                // Geçen süreyi saniye cinsinden yazdır
                Debug.WriteLine("Execution Time: " + stopwatch.Elapsed.TotalMilliseconds + " miliseconds " + "\t" + ip_adress);


                Invoke((Action)(() =>
                {
                    textBox1.Text = ip_adress;
                }));

                // Reset the current machine index to indicate completion
                currentMachineIndex = -1;
                await Task.Delay(10); // 10 milisaniye bekleyerek database çakışmalarını önle
            }
        }

        public static bool PingHost(string nameOrAddress)
        {
            bool pingable = false;
            Ping pinger = null;

            try
            {
                pinger = new Ping();
                PingReply reply = pinger.Send(nameOrAddress);
                pingable = reply.Status == IPStatus.Success;
            }
            catch (PingException)
            {
                // Discard PingExceptions and return false;
            }
            finally
            {
                if (pinger != null)
                {
                    pinger.Dispose();
                }
            }

            return pingable;
        }

        private static void SaveNewCycleTimeData(int? machineID, DateTime productionDate, int? productionCount, float cycleTime)
        {
            using (var context = new ernamas_dijitalEntities())
            {
                var newLog = new MachineProductionDatas
                {
                    MachineID = Convert.ToInt32(machineID),
                    ProductionDate = productionDate,
                    ProductionCount = Convert.ToInt32(productionCount),
                    CycleTime = cycleTime
                };

                context.MachineProductionDatas.Add(newLog);
                context.SaveChanges();
            }
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            // Cancel the tasks when the form is closing
            cts.Cancel();
        }



        // VERİ OPTİMİZASYON VE GRAFİK OLUŞTURMA
        private void btnJson_Click(object sender, EventArgs e)
        {
            string jsonFilePath = @"C:\Users\alper\source\repos\ERN028_MakinaVeriToplama\Resources\synthetic_data2.json";
            if (!File.Exists(jsonFilePath))
            {
                MessageBox.Show("JSON file not found.");
                return;
            }

            // JSON dosyasını okuma
            var jsonData = File.ReadAllText(jsonFilePath);
            var data = JsonConvert.DeserializeObject<SyntheticData>(jsonData);

            // Zaman etiketlerini ve veriyi elde etme
            var times = data.Labels.Select(t => TimeToSeconds(t)).ToArray();
            var values = data.Data.ToArray();

            // Veriyi optimize etme
            var optimizedData = OptimizeDataWithAveraging(times, values, 25);

            // Optimize edilmiş veriyi spline eğrisi ile modelleme
            var spline = CubicSpline.InterpolateAkima(optimizedData.Item1, optimizedData.Item2);
            //var spline = CubicSpline.InterpolateAkima(times, values);

            // Yeni zaman aralıkları oluşturma ve spline eğrisini değerlendirme
            var timesNew = Linspace(optimizedData.Item1.Min(), optimizedData.Item1.Max(), 1200);
            var valuesNew = timesNew.Select(t => spline.Interpolate(t)).ToArray();

            // Boşlukları tespit etme
            var gaps = DetectGaps(optimizedData.Item1, 120);

            // Eğriyi gösterme
            //PlotData(times, values, timesNew, valuesNew);

            // Yeni form penceresi açma ve grafiği gösterme
            //GraphForm graphForm = new GraphForm(times, values, timesNew, valuesNew, gaps);
            //graphForm.Show();
        }

        private double TimeToSeconds(string time)
        {
            var parts = time.Split(':').Select(int.Parse).ToArray();
            return parts[0] * 3600 + parts[1] * 60 + parts[2];
        }

        private double[] Linspace(double start, double end, int num)
        {
            double[] result = new double[num];
            double step = (end - start) / (num - 1);
            for (int i = 0; i < num; i++)
            {
                result[i] = start + i * step;
            }
            return result;
        }

        private Tuple<double[], double[]> OptimizeDataWithAveraging(double[] times, double[] values, double threshold)
        {
            var optimizedTimes = new List<double>();
            var optimizedValues = new List<double>();

            double interval = 60; // Ortalama almak için 1 dakikalık aralık (60 saniye)
            double currentIntervalStart = times[0];
            double sum = 0;
            int count = 0;

            for (int i = 0; i < times.Length; i++)
            {
                if (times[i] < currentIntervalStart + interval)
                {
                    if (values[i] <= threshold * 1.1)
                    {
                        sum += values[i];
                        count++;
                    }
                    else
                    {
                        optimizedTimes.Add(times[i]);
                        optimizedValues.Add(values[i]);
                    }
                }
                else
                {
                    if (count > 0)
                    {
                        optimizedTimes.Add(currentIntervalStart + interval / 2); // Aralığın ortası
                        optimizedValues.Add(sum / count);
                    }

                    // Yeni aralığa geç
                    currentIntervalStart += interval;
                    sum = values[i] <= threshold * 1.1 ? values[i] : 0;
                    count = values[i] <= threshold * 1.1 ? 1 : 0;

                    if (values[i] > threshold * 1.1)
                    {
                        optimizedTimes.Add(times[i]);
                        optimizedValues.Add(values[i]);
                    }
                }
            }

            // Son aralığı ekle
            if (count > 0)
            {
                optimizedTimes.Add(currentIntervalStart + interval / 2);
                optimizedValues.Add(sum / count);
            }

            return new Tuple<double[], double[]>(optimizedTimes.ToArray(), optimizedValues.ToArray());
        }

        private void SaveOptimizedDataToDatabase(int machineId, double[] times, double[] values)
        {
            var timesJson = JsonConvert.SerializeObject(times);
            var valuesJson = JsonConvert.SerializeObject(values);

            using (var connection = new SqlConnection("YourConnectionStringHere"))
            {
                var query = "INSERT INTO SplineData (MachineId, TimePoints, ValuePoints) VALUES (@MachineId, @TimePoints, @ValuePoints)";
                using (var command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@MachineId", machineId);
                    command.Parameters.AddWithValue("@TimePoints", timesJson);
                    command.Parameters.AddWithValue("@ValuePoints", valuesJson);

                    connection.Open();
                    command.ExecuteNonQuery();
                }
            }
        }

        private List<Tuple<double, double>> DetectGaps(double[] times, double gapThreshold)
        {
            var gaps = new List<Tuple<double, double>>();

            for (int i = 1; i < times.Length; i++)
            {
                if (times[i] - times[i - 1] >= gapThreshold)
                {
                    gaps.Add(new Tuple<double, double>(times[i - 1], times[i]));
                }
            }
            return gaps;
        }


        // GECE YARISI SENARYOSU

        private void ScheduleNightlyTask()
        {
            TimeSpan scheduledTime = new TimeSpan(23, 59, 0); // Gece 2:00 için planlama

            Task.Run(async () =>
            {
                while (true)
                {
                    TimeSpan currentTime = DateTime.Now.TimeOfDay;
                    if (currentTime > scheduledTime)
                    {
                        // Gece 2'yi geçtiyse, işlemi çalıştır
                        RunNightlyTask();
                        // Bir sonraki günün 2:00'sine kadar bekle
                        await Task.Delay((24 * 60 * 60 * 1000) - (int)currentTime.TotalMilliseconds);
                    }
                    else
                    {
                        // Gecenin 2'sine kadar bekle
                        await Task.Delay((int)(scheduledTime - currentTime).TotalMilliseconds);
                    }
                }
            });
        }

        private void RunNightlyTask()
        {
            try
            {
                SaveAllDailyDataToHistory().Wait();
            }
            catch (Exception ex)
            {
                // Hataları loglayın
                LogError(ex);
                // Gerekirse uygulamayı yeniden başlatın veya kullanıcıyı bilgilendirin
            }
        }

        // Günlük verileri history tablolarına kaydetme ana fonksiyonu
        private async Task SaveAllDailyDataToHistory()
        {
            // Hesaplama işlemlerini burada gerçekleştirin
            try
            {
                // Veriyi veri tabanından çekme işlemi
                var dailyData = await FetchDailyDataAsync();

                // Veriyi işleme ve geçmişe kaydetme
                await ProcessAndSaveDataAsync(dailyData);

                // İşlem tamamlandığında kullanıcıya bilgi ver
                LogMessage("Günlük veri başarılı bir şekilde geçmişe kaydedildi.");
            }
            catch (Exception ex)
            {
                // Hata durumunda loglama yap
                LogError(ex);
                // Kullanıcıya hata bildiriminde bulun
                LogMessage("Günlük veriyi kaydederken bir hata oluştu.");
            }

        }

        // Günlük veriyi veri tabanından çekme işlemi
        private async Task<List<CycleData>> FetchDailyDataAsync()
        {
            using (var context = new ernamas_dijitalEntities())
            {
                //var today = DateTime.Today;
                DateTime today = new DateTime(2024, 05, 24);

                var startOfDay = today.Date;
                var endOfDay = today.Date.AddDays(1).AddTicks(-1);

                var data = await context.MachineProductionDatas
                                        .Where(l => l.ProductionDate >= startOfDay && l.ProductionDate <= endOfDay)
                                        .ToListAsync();

                // Convert to CycleData
                var cycleDataList = data.Select(d => new CycleData
                {
                    MachineID = d.MachineID,
                    ProductionCount = d.ProductionCount,
                    ProductionDate = d.ProductionDate,
                    CycleTime = d.CycleTime
                }).ToList();

                return cycleDataList;
            }
        }

        // Veriyi işleme ve geçmişe kaydetme işlemi
        private async Task ProcessAndSaveDataAsync(List<CycleData> dailyData)
        {
            int machineCount = AdressList.Count;

            for (int machine_index = 1; machine_index < machineCount + 1; machine_index++)
            {
                var filteredList = dailyData.Where(c => c.MachineID == machine_index).ToList();

                string filtredJsonList = ConvertToJson(filteredList);

                double averageCycleTime = CalculateCycleTimeAverage(filtredJsonList);


                // O günün sonundaki mevcut bilgileri kaydetme ve optimize cycle time verilerini kaydetme
                using (var context = new ernamas_dijitalEntities())
                {
                    var machineCurrent = context.MachineCurrentStatus.FirstOrDefault(l => l.MachineID == machine_index);

                    // Veriyi geçmiş tablosuna ekleme
                    var historicalData = new MachineHistoryStatus
                    {
                        DateDay = DateTime.Today,
                        MachineID = machine_index,
                        AverageCycleTime = Math.Round(averageCycleTime, 3),
                        ProducedItems = machineCurrent.ProducedItems,
                        OKProductCount = machineCurrent.OKProductCount,
                        NGProductCount = machineCurrent.NGProductCount,
                        OKRatio = machineCurrent.OKRatio,
                        CycleTimeFormula = filtredJsonList
                    };
                    context.MachineHistoryStatus.Add(historicalData);

                    await context.SaveChangesAsync();

                    // MEVCUT CYCLE TIME VERİLERİNİN SİLİNMESİ yeni güne hazır hale getirilmesi

                }
            }
        }

        // İstenilen günün ve istenilen makinenin tablosu ayrıştırılmış şekilde gelir ve json formatında çevirilmek üzere dönüştürülür 
        private string ConvertToJson(List<CycleData> cycleDataList)
        {
            var sortedCycleDataList = cycleDataList.OrderBy(cd => cd.ProductionDate).ToList();

            var jsonData = new
            {
                time = sortedCycleDataList.Select(cd => cd.ProductionDate.ToString("HH:mm:ss")).ToArray(),
                cycle_time = sortedCycleDataList.Select(cd => cd.CycleTime).ToArray()
            };

            return JsonConvert.SerializeObject(jsonData);
        }

        // json formatımızdaki verinin içerisindeki cycle time verileri ile ortalama cycle time hesaplar
        private double CalculateCycleTimeAverage(string jsonString)
        {
            // JSON stringini ayrıştırma
            var jsonData = JObject.Parse(jsonString);

            // cycle_time verilerini elde etme
            var cycleTimes = jsonData["cycle_time"].Select(ct => (double)ct).ToList();

            // Ortalama hesaplama
            return cycleTimes.Average();
        }

        private void LogMessage(string message)
        {
            // Log mesajını dosyaya veya veri tabanına yazın
            File.AppendAllText("log.txt", $"{DateTime.Now}: {message}\n");
        }

        private void LogError(Exception ex)
        {
            // Hata mesajını loglayın
            File.AppendAllText("error_log.txt", $"{DateTime.Now}: {ex.Message}\n{ex.StackTrace}\n");
        }

        private void textBox1_TextChanged(object sender, EventArgs e)
        {

        }
    }

    public class SyntheticData // Silinecek! Grafik testleri için class
    {
        public List<string> Labels { get; set; }
        public List<double> Data { get; set; }
    }
    public class MachineInfo
    {
        public string IPAddress { get; set; }
        public int Port { get; set; }
        public int ExpectedCycleTime { get; set; }
    }
    public class CycleData
    {
        public System.DateTime ProductionDate { get; set; }
        public int ProductionCount { get; set; }
        public double CycleTime { get; set; }
        public int MachineID { get; set; }
    }
}
