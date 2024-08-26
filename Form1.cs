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
        private Dictionary<int, MachineInfo> AdressList = new Dictionary<int, MachineInfo>(); // Silinecek
        private Dictionary<int, MachineInfo> MachineList = new Dictionary<int, MachineInfo>();

        private string ip_adress;

        int[] registers = new int[100];

        int currnetProduction; // Production : DWORD : D200 : 200
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

        StreamWriter logWriter = new StreamWriter("ModbusLog.txt", true);

        private System.Timers.Timer summaryTimer;

        public Form1()
        {
            InitializeComponent();
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            // Timer Tick olayını başlat
            timer1.Tick += new EventHandler(CheckTimeForSummary);

            // Timer'ı her 10 dakikada bir çalışacak şekilde ayarla
            timer1.Interval = 600000; // 10 dakika (600.000 milisaniye) aralıklarla çalışacak
            timer1.Start();
        }

        private void btnReadData_Click_1(object sender, EventArgs e)
        {
            ReadMachineData(); // Veri tabanındaki veriler ile MachineList içerisini doldurur

            //AllMachineIpTest(); // MachineList'deki tüm makine ip adreslerine ping testi yapar MessageBox ile gösterir

            OneMachineIpTest("10.10.16.30"); // Tek ip adresine ping testi yapar MessageBox ile gösterir

            AllMachineModbusConnection(); // Tüm makinelere ModBus bağlantısı yapar ve nesneye Client'ti atar

            MainLoop();
        }

        public void MainLoop()
        {
            while (true) 
            {
                foreach (var machine in MachineList.Values)
                {
                    if (machine.MachineModbusClient != null)
                    {
                        if (machine.MachineModbusClient.Connected)
                        {
                            ModbusDataRead(machine.MachineModbusClient);

                            //WriteDataBase(); // ModbusDataRead() fonksiyonunun içinde en sonunda çağırılıyor Error çıkmasına rağmen devam etmesini önlemek için
                        }
                        else // Bağlantı kopmuşsa
                        {
                            // Yeniden bağlanıp loop'a kaldığı yerden devam edecek
                        }
                    }
                    Thread.Sleep(200); // Çoklu istekler ve yüksek trafik kaynaklı uygulama çökmesine karşı
                }         
            }
        }
        public void AllMachineModbusConnection() 
        {
            foreach (var machine in MachineList.Values)
            {
                machine.MachineModbusClient = ModbusConnection(machine.IPAddress, machine.Port);
            }
        }

        public ModbusClient ModbusConnection(string ip_adress, int port)
        {
            try
            {
                modbusClient = new ModbusClient(ip_adress, port);
                modbusClient.Connect();
                return modbusClient;
            }
            catch (Exception e)
            {
                logWriter.WriteLine($"{ip_adress} while connection: Exception: {e}");
                return null;
            }
        }

        public void ModbusDataRead(ModbusClient modbusClient)
        {
            var stopwatch = new Stopwatch();
            stopwatch.Start();

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

                WriteDataBase();

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
                //logWriter.WriteLine($"{DateTime.Now}: Modbus read operation took {stopwatch.ElapsedMilliseconds} ms");
            }
        }

        public void WriteDataBase()
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

                    if (differenceOfProduction == 1) // Eğer artış olmuşsa
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

        public void ReadMachineData() 
        {
            using (var context = new ernamas_dijitalEntities())
            {
                var machines = context.Machines.ToList();

                foreach (var machine in machines)
                {
                    MachineList.Add(machine.MachineID, new MachineInfo
                    {
                        MachineName = machine.MachineIdentifier,
                        IPAddress = machine.IPAdresses,
                        Port = Convert.ToInt32(machine.PortAdress), // Port bilgisini buradan alıyoruz
                        ExpectedCycleTime = Convert.ToInt32(machine.ExpectedCycleTime)
                    });
                }
            }
        }

        public void AllMachineIpTest()
        {
            StringBuilder results = new StringBuilder();

            foreach (var machine in MachineList.Values)
            {
                bool isPingable = PingHost(machine.IPAddress);
                string result = $"{machine.IPAddress}: {(isPingable ? "Reachable" : "Not Reachable")}";
                results.AppendLine(result);
            }

            MessageBox.Show(results.ToString(), "Ping Test Results", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        public void OneMachineIpTest(string ipAdress)
        {
            StringBuilder results = new StringBuilder();

            bool isPingable = PingHost(ipAdress);
            string result = $"{ipAdress}: {(isPingable ? "Reachable" : "Not Reachable")}";
            results.AppendLine(result);

            MessageBox.Show(results.ToString(), "Ping Test Results", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        private void CheckTimeForSummary(object sender, EventArgs e)
        {
            // Şu anki saati kontrol et
            DateTime now = DateTime.Now;

            // Eğer saat 18:00 ve 18:10 arasındaysa özetleme işlemini başlat
            if (now.Hour == 16 && now.Minute >= 25 && now.Minute < 50)
            {
                PerformDailySummary();
            }
        }

        private void PerformDailySummary()
        {
            try
            {
                using (var context = new ernamas_dijitalEntities())
                {
                    // 1. MachineCurrentStatus tablosundaki verileri al
                    var currentStatusList = context.MachineCurrentStatus.ToList();

                    // 2. Her bir kayıt için MachineHistoryStatus tablosuna ekleme yap
                    foreach (var currentStatus in currentStatusList)
                    {
                        var historyStatus = new MachineHistoryStatus
                        {
                            DateDay = DateTime.Today,
                            MachineID = currentStatus.MachineID,
                            AverageCycleTime = currentStatus.AverageCycleTime,
                            ProducedItems = currentStatus.ProducedItems,
                            OKProductCount = currentStatus.OKProductCount,
                            NGProductCount = currentStatus.NGProductCount,
                            OKRatio = currentStatus.OKRatio,
                            CycleTimeFormula = "{}",
                        };

                        context.MachineHistoryStatus.Add(historyStatus);

                        // Hurda verileri ekleme kısmı
                        if (currentStatus.NGProductCount > 0)
                        {
                            var lossQuality = new MachineLossQualities
                            {
                                MachineID = currentStatus.MachineID,
                                FaultInfo = "",
                                MachineFaultTypeID = 1,
                                LossQuantity = currentStatus.NGProductCount,
                                LossDate = DateTime.Today,
                                employeeId = 0,
                            };
                        }
                    }

                    // 3. Veritabanında değişiklikleri kaydet
                    context.SaveChanges();

                    MessageBox.Show("Günlük özetleme işlemi başarıyla gerçekleştirildi.", "Özetleme", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Özetleme işlemi sırasında bir hata oluştu: {ex.Message}", "Hata", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            // Özetleme işlemi tamamlandıktan sonra timer'ı durdurup ertesi gün için ayarlayabilirsiniz
            timer1.Stop();

            // Timer'ı ertesi gün için yeniden başlatmak isterseniz:
            timer1.Interval = 86400000; // 24 saat bekler
            timer1.Start();
        }



        // MODBUS İLE VERİ TOPLAMA // ----------------------------------------------------------------------------------- //

        private static int intervalBetweenMachines = 800; // milliseconds aralıklarla makinalara veri isteği atıyor

        private int currentMachineIndex = -1;

        private CancellationTokenSource cts = new CancellationTokenSource();

        //private void btnReadData_Click_1(object sender, EventArgs e)
        //{
        //    //string temp_ip_for_test = "10.10.16.70";
        //    //if (PingHost(temp_ip_for_test)) MessageBox.Show($"Ping test is Successful\n for: " + temp_ip_for_test);
        //    //else MessageBox.Show($"Ping test is Unsuccessful " + temp_ip_for_test);

        //    StartReadingData();
        //}

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

    }
    public class MachineInfo
    {
        public string MachineName { get; set; }
        public string IPAddress { get; set; }
        public int Port { get; set; }
        public int ExpectedCycleTime { get; set; }
        public ModbusClient MachineModbusClient { get; set; }
    }
    public class CycleData
    {
        public System.DateTime ProductionDate { get; set; }
        public int ProductionCount { get; set; }
        public double CycleTime { get; set; }
        public int MachineID { get; set; }
    }
}
