using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Net.NetworkInformation;
using System.Threading;
using EasyModbus;
using System.IO;
using System.Diagnostics;


namespace ERN028_MakinaVeriTopalamaFormsApp
{
    public partial class Form1 : Form
    {
        private Dictionary<int, MachineInfo> MachineList = new Dictionary<int, MachineInfo>();

        PingTest pingTest = new PingTest();

        StreamWriter logWriter = new StreamWriter("ModbusLog.txt", true);

        private System.Timers.Timer summaryTimer;

        private TimeSpan summaryStartTime = new TimeSpan(18, 0, 0); // 18:00


        public Form1()
        {
            InitializeComponent();
        }

        private void btnReadData_Click_1(object sender, EventArgs e)
        {
            ReadMachineData(); // Veri tabanındaki veriler ile (GLOBAL) MachineList oluşturur

            AllMachineIpTest(); // MachineList'deki tüm ip adreslerine ping testi yapar

            OneMachineIpTest("10.10.16.30");

            AllMachineModbusConnection(); // Tüm makinelere ModBus bağlantısı yapar ve nesneye Client'ti atar

            MainLoop(); // Sırayla tüm makinelerden veri alır
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
                            ModbusDataRead(machine);
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
                ModbusClient modbusClient = new ModbusClient(ip_adress, port);
                modbusClient.Connect();
                return modbusClient;
            }
            catch (Exception e)
            {
                logWriter.WriteLine($"{ip_adress} while connection: Exception: {e}");
                return null;
            }
        }

        public void ModbusDataRead(MachineInfo machine)
        {
            ModbusClient modbusClient = machine.MachineModbusClient; 
            var stopwatch = new Stopwatch();
            stopwatch.Start();

            try
            {
                int startAddress = 200;
                int numRegisters = 10;

                //logWriter.WriteLine(); // Bir satır boşluk bırakır
                //logWriter.WriteLine($"{DateTime.Now}: Modbus Client Connected: {modbusClient.Connected}");
                int[] registers = modbusClient.ReadHoldingRegisters(startAddress, numRegisters);

                machine.MachineData.CurrentProduction = registers[0]; // Production : DWORD : D200 : 200
                machine.MachineData.OKProductCount = registers[2]; // OK qty : DWORD : D202 : 202
                machine.MachineData.NGProductCount = registers[4]; // NG qty : DWORD : D204 : 204
                machine.MachineData.OKRatio = registers[8]; // OK Ratio : DWORD : D208 : 208
                machine.MachineData.CycleTime = registers[6] / 10; // Current Beat : DWORD : D206 : 206

                int statusAddress = 30730;
                int statusNumRegisters = 20;
                bool[] statusRegisters = modbusClient.ReadCoils(statusAddress, statusNumRegisters);

                machine.MachineData.Run = statusRegisters[10]; // Run : BOOL : B10 : 30740
                machine.MachineData.Auto = statusRegisters[12]; // Auto/Manuel : BOOL : B12 : 30742 -> (False: Manuel / True: Auto)
                machine.MachineData.Ready = statusRegisters[8]; // Ready : BOOL : B08 : 30738
                machine.MachineData.Fault = statusRegisters[15]; // Fault : BOOL : B15 : 30745

                //logWriter.WriteLine($"{DateTime.Now}: Current Production: {currnetProduction}");
                //logWriter.WriteLine($"{DateTime.Now}: Cycle Time: {CycleTime}");

                WriteDataBase(machine);

            }
            catch (System.IndexOutOfRangeException ex)
            {
                logWriter.WriteLine($"{DateTime.Now}: Exception: System.IndexOutOfRangeException");
                logWriter.WriteLine($"{DateTime.Now}: Message: {ex.Message}");
                logWriter.WriteLine($"{DateTime.Now}: StackTrace: {ex.StackTrace}");

                ReconnectModbusClient(modbusClient);
            }
            catch (System.IO.IOException ex)
            {
                logWriter.WriteLine($"{DateTime.Now}: Exception: System.IO.IOException");
                logWriter.WriteLine($"{DateTime.Now}: Message: {ex.Message}");
                logWriter.WriteLine($"{DateTime.Now}: StackTrace: {ex.StackTrace}");

                ReconnectModbusClient(modbusClient);
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

        public void WriteDataBase(MachineInfo machine)
        {
            int temp_Status = 0;
            using (var context = new ernamas_dijitalEntities())
            {
                var machineCurrentStatus = context.MachineCurrentStatus.FirstOrDefault(l => l.MachineID == machine.MachineID);

                if (machineCurrentStatus != null)
                {
                    // Karşılaştırma için bir önceki kayıttaki veriler
                    int lastProduction = Convert.ToInt32(machineCurrentStatus.ProducedItems);
                    int lastStatusTypeID = Convert.ToInt32(machineCurrentStatus.StatusTypeID);

                    // Makine artış kontrolü
                    int differenceOfProduction = (machine.MachineData.CurrentProduction - lastProduction);

                    if (differenceOfProduction == 1) // Eğer 1 artış olmuşsa
                    {
                        // O anki artış ile tablodaki değeri güncelle
                        machineCurrentStatus.CurrentDailyProduction = Convert.ToInt32(machineCurrentStatus.CurrentDailyProduction) + 1; // Eğer artış varsa bir arttır

                        // Cycle Time tabloya tek tek kayıt
                        SaveNewCycleTimeData(machineCurrentStatus.MachineID, DateTime.Now, machineCurrentStatus.CurrentDailyProduction, machine.MachineData.CycleTime);
                    }

                    // Hesaplama yapmak için geçici durum bilgisi atama
                    if (machine.MachineData.Ready)
                    {
                        temp_Status = 1; // Ready
                    }
                    else if (machine.MachineData.Run)
                    {
                        temp_Status = 2; // Run
                    }
                    else if (machine.MachineData.Fault)
                    {
                        temp_Status = 4; // Fault
                    }
                    else
                    {
                        temp_Status = 5; // Invalid
                    }

                    // MachineCurrentStatus Bilgilerin Güncellenmesi
                    machineCurrentStatus.ProducedItems = machine.MachineData.CurrentProduction;
                    machineCurrentStatus.CycleTime = machine.MachineData.CycleTime;
                    machineCurrentStatus.OKProductCount = machine.MachineData.OKProductCount;
                    machineCurrentStatus.NGProductCount = machine.MachineData.NGProductCount;
                    machineCurrentStatus.AverageCycleTime = 0;
                    machineCurrentStatus.OKRatio = machine.MachineData.OKRatio;
                    machineCurrentStatus.Timestamp = DateTime.Now;
                    machineCurrentStatus.StatusTypeID = temp_Status;

                    // MachineChangeLogs için Statü değişiminde kontrol
                    if (lastStatusTypeID != temp_Status)
                    {
                        var newMachineChangeLog = new MachineChangeLogs
                        {
                            MachineID = machineCurrentStatus.MachineID,
                            Timestamp = DateTime.Now,
                            PreviousValue = lastStatusTypeID,
                            ChangeValue = temp_Status
                        };

                        context.MachineChangeLogs.Add(newMachineChangeLog);

                        // Makine tekrar çalışma durumuna geçtiğince ne zaman durmuştu kontrolü yapan bölüm
                        if (temp_Status == 2 && lastStatusTypeID != 2)
                        {
                            // Makine ID'sine göre tüm kayıtları al ve zamana göre sıralı listeyi oluştur
                            var machineChangeLogs = context.MachineChangeLogs.Where(l => l.MachineID == machine.MachineID)
                                                            .OrderByDescending(l => l.Timestamp).ToList();

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

                    context.SaveChanges();
                }
            }
        }

        public void ReconnectModbusClient(ModbusClient modbusClient)
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
                        MachineID = machine.MachineID,
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
                bool isPingable = pingTest.PingHost(machine.IPAddress);
                string result = $"{machine.IPAddress}: {(isPingable ? "Reachable" : "Not Reachable")}";
                results.AppendLine(result);
            }

            MessageBox.Show(results.ToString(), "Ping Test Results", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        public void OneMachineIpTest(string ipAdress)
        {
            StringBuilder results = new StringBuilder();

            bool isPingable = pingTest.PingHost(ipAdress);
            string result = $"{ipAdress}: {(isPingable ? "Reachable" : "Not Reachable")}";
            results.AppendLine(result);

            MessageBox.Show(results.ToString(), "Ping Test Results", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            // Timer Tick olayını başlat
            timer1.Tick += new EventHandler(CheckTimeForSummary);

            // Timer'ı her 10 dakikada bir çalışacak şekilde ayarla
            timer1.Interval = 600000; // 10 dakika (600.000 milisaniye) aralıklarla kontrol edecek
            timer1.Start();
        }

        private void CheckTimeForSummary(object sender, EventArgs e)
        {
            // Şu anki saati kontrol et
            TimeSpan now = DateTime.Now.TimeOfDay;

            // summaryEndTime, summaryStartTime'a 10 dakika eklenmiş hali olarak tanımlandı
            TimeSpan summaryEndTime = summaryStartTime.Add(new TimeSpan(0, 10, 0));

            // Eğer saat aralığı içindeyse özetleme işlemini başlat
            if (now >= summaryStartTime && now < summaryEndTime)
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
                    // MachineCurrentStatus tablosundaki verileri al
                    var currentStatusList = context.MachineCurrentStatus.ToList();

                    // Her bir kayıt için MachineHistoryStatus tablosuna ekleme yap
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

                    // Veritabanında değişiklikleri kaydet
                    context.SaveChanges();

                    MessageBox.Show("Günlük özetleme işlemi başarıyla gerçekleştirildi.", "Özetleme", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Özetleme işlemi sırasında bir hata oluştu: {ex.Message}", "Hata", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            // Özetleme işlemi tamamlandıktan sonra timer durup ertesi gün için yeniden kurulmuş olur
            timer1.Stop();

            timer1.Interval = 86400000; // 24 saat bekler
            timer1.Start();
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
    }
    public class MachineInfo
    {
        public int MachineID { get; set; }
        public string MachineName { get; set; }
        public string IPAddress { get; set; }
        public int Port { get; set; }
        public int ExpectedCycleTime { get; set; }
        public ModbusClient MachineModbusClient { get; set; }
        public MachineData MachineData { get; set; }
    }
    public class MachineData
    {
        public int CurrentProduction { get; set; }
        public int OKProductCount { get; set; }
        public int NGProductCount { get; set; }
        public float OKRatio { get; set; }
        public float CycleTime { get; set; }

        public bool Auto { get; set; }
        public bool Run { get; set; }
        public bool Ready { get; set; }
        public bool Fault { get; set; }
    }
}
