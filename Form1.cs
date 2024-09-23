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
using System.Data.SqlClient;
using System.Threading.Tasks;


namespace ERN028_MakinaVeriTopalamaFormsApp
{
    public partial class Form1 : Form
    {
        private Dictionary<int, MachineInfo> MachineList = new Dictionary<int, MachineInfo>();

        PingTest pingTest = new PingTest();

        private string LogFilePath = "ModbusLog.txt";

        private System.Timers.Timer summaryTimer;

        private CancellationTokenSource cancellationTokenSource;

        public Form1()
        {
            InitializeComponent();
            cancellationTokenSource = new CancellationTokenSource();
        }

        private void btnReadData_Click_1(object sender, EventArgs e)
        {
            btnReadData.Enabled = false; // Butonu devre dışı bırak

            ReadMachineData(); // Veri tabanındaki veriler ile (GLOBAL) MachineList oluşturur

            // AllMachinPingTest(); // MachineList'deki tüm ip adreslerine ping testi yapar

            // OneMachinePingTest("10.10.16.30");

            AllMachineModbusConnection(); // Tüm makinelere ModBus bağlantısı yapar (ve nesneye Client'ti atar)

            Task.Run(() => MainLoop(cancellationTokenSource.Token)); // Döngüyü arka planda çalıştır
        }

        public void MainLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                foreach (var machine in MachineList.Values)
                {
                    UpdateUI(machine);

                    if (machine.MachineModbusClient != null)
                    {
                        if (machine.MachineModbusClient.Connected)
                        {
                            ModbusDataRead(machine);
                        }
                        else // bağlantı kurulmamış ise
                        {
                            ReconnectModbusClient(machine.MachineModbusClient);
                        }
                    }
                    else
                    {
                        machine.MachineModbusClient = ModbusConnection(machine);
                    }
                    Thread.Sleep(200); // Çoklu istekler ve yüksek trafik kaynaklı bağlantı çökmesine karşı
                }
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            cancellationTokenSource.Cancel(); // Döngüyü sonlandır
            base.OnFormClosing(e);
        }

        public void AllMachineModbusConnection()
        {
            foreach (var machine in MachineList.Values)
            {
                machine.MachineModbusClient = ModbusConnection(machine);
            }
        }

        public ModbusClient ModbusConnection(MachineInfo machine)
        {
            try
            {
                ModbusClient modbusClient = new ModbusClient(machine.IPAddress, machine.Port);
                modbusClient.Connect();
                return modbusClient;
            }
            catch (Exception e)
            {
                if (!string.IsNullOrEmpty(e.Message)) 
                {
                    //LogText($"{machine.IPAddress}\t Error while connection: Exception: {e.Message}"); // Log belgesini okunamaz hale getirdiği için kapatıldı
                }

                WriteDataBaseDisconnection(machine);

                return null;
            }
        }

        public void ModbusDataRead(MachineInfo machine)
        {
            var stopwatch = new Stopwatch();
            stopwatch.Start();

            try
            {
                int startAddress = 200;
                int numRegisters = 20;

                int[] registers = machine.MachineModbusClient.ReadHoldingRegisters(startAddress, numRegisters);

                machine.MachineData.CurrentProduction = registers[0];           // Production           : DWORD : D200 : 200
                machine.MachineData.OKProductCount = registers[2];              // OK qty               : DWORD : D202 : 202
                machine.MachineData.NGProductCount = registers[4];              // NG qty               : DWORD : D204 : 204
                machine.MachineData.CycleTime = registers[6] / 10;              // Cycle Time           : DWORD : D206 : 206
                machine.MachineData.OKRatio = registers[8];                     // OK Ratio             : DWORD : D208 : 208
                machine.MachineData.SecondCycleTime = registers[10] / 10;       // Second Cycle Time    : DWORD : D208 : 210

                int statusAddress = 30720;
                int statusNumRegisters = 100;
                
                bool[] statusRegisters = machine.MachineModbusClient.ReadCoils(statusAddress, statusNumRegisters);

                machine.MachineData.Run = statusRegisters[58];      // Run          : BOOL : B10 : 30740
                machine.MachineData.Auto = statusRegisters[22];     // Auto/Manuel  : BOOL : B12 : 30742 -> (False: Manuel / True: Auto)
                machine.MachineData.Ready = statusRegisters[18];    // Ready        : BOOL : B08 : 30738
                machine.MachineData.Fault = statusRegisters[25];    // Fault        : BOOL : B15 : 30745

                //LogText(Convert.ToString(machine.IPAddress)
                //    + '\n' + Convert.ToString("Run   :\t" + machine.MachineData.Run)
                //    + '\n' + Convert.ToString("Ready :\t" + machine.MachineData.Ready)
                //    + '\n' + Convert.ToString("Fault :\t" + machine.MachineData.Fault) 
                //    + '\n' + Convert.ToString("Prodt :\t" + machine.MachineData.CurrentProduction) 
                //    + '\n' + Convert.ToString("CycTi :\t" + machine.MachineData.CycleTime) 
                //    + '\n');

                WriteDataBase(machine);

            }
            catch (System.IndexOutOfRangeException ex)
            {
                LogText($"{machine.IPAddress}\t Exception: System.IndexOutOfRangeException");
                LogText($"{machine.IPAddress}\t Message: {ex.Message}");
                LogText($"{machine.IPAddress}\t StackTrace: {ex.StackTrace}");

                ReconnectModbusClient(machine.MachineModbusClient);
            }
            catch (System.IO.IOException ex)
            {
                LogText($"{machine.IPAddress}\t Exception: System.IO.IOException");
                LogText($"{machine.IPAddress}\t Message: {ex.Message}");
                LogText($"{machine.IPAddress}\t StackTrace: {ex.StackTrace}");

                ReconnectModbusClient(machine.MachineModbusClient);
            }
            catch (Exception e)
            {
                LogText($"{machine.IPAddress}\t General Exception Caught");
                LogText($"{machine.IPAddress}\t Message: {e.Message}");
                LogText($"{machine.IPAddress}\t StackTrace: {e.StackTrace}");
            }
            finally
            {
                stopwatch.Stop();
                //logWriter.WriteLine($"{DateTime.Now}: {machine.IPAddress}: Modbus read operation took {stopwatch.ElapsedMilliseconds} ms");
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
                    int lastOKProduction = Convert.ToInt32(machineCurrentStatus.CurrentDailyOKProduction);
                    int lastNGProduction = Convert.ToInt32(machineCurrentStatus.CurrentDailyNGProduction);
                    int lastStatusTypeID = Convert.ToInt32(machineCurrentStatus.StatusTypeID);

                    // Makine artış kontrolleri
                    int differenceOfProduction = (machine.MachineData.CurrentProduction - lastProduction);
                    if (differenceOfProduction < 3) //Eğer artış 3 ün altındaysa (güvenli bir artış) bu artışı CurrentDailyProduction 'a ekle
                    {
                        // O anki artış ile tablodaki değeri güncelle
                        machineCurrentStatus.CurrentDailyProduction = Convert.ToInt32(machineCurrentStatus.CurrentDailyProduction) + differenceOfProduction;
                        // Cycle Time tabloya tek tek kayıt
                        SaveNewCycleTimeData(machineCurrentStatus.MachineID, DateTime.Now, machineCurrentStatus.CurrentDailyProduction, machine.MachineData.CycleTime);
                    }

                    int differenceOfOK = (machine.MachineData.OKProductCount - lastOKProduction);
                    if (differenceOfOK == 1)
                    {
                        machineCurrentStatus.CurrentDailyOKProduction = Convert.ToInt32(machineCurrentStatus.CurrentDailyOKProduction) + 1;
                    }

                    int differenceOfNG = (machine.MachineData.NGProductCount - lastNGProduction);
                    if (differenceOfNG == 1)
                    {
                        machineCurrentStatus.CurrentDailyNGProduction = Convert.ToInt32(machineCurrentStatus.CurrentDailyNGProduction) + 1;
                    }

                    // Hesaplama yapmak için geçici durum bilgisi atama
                    if (machine.MachineData.Run)
                    {
                        temp_Status = 2; // Run
                    }
                    else if (machine.MachineData.Ready)
                    {
                        temp_Status = 1; // Ready
                    }
                    else if (machine.MachineData.Fault)
                    {
                        temp_Status = 4; // Fault
                    }
                    else
                    {
                        temp_Status = 5; // Invalid
                    }

                    // İki taraflı cycle time bilgisi için ortalama hesabı
                    if (machine.MachineData.SecondCycleTime != 0)
                    {
                        if (machine.MachineData.CycleTime != 0)
                        {
                            float temp_cycle_time = (machine.MachineData.CycleTime + machine.MachineData.SecondCycleTime) / 2;
                            machine.MachineData.CycleTime = temp_cycle_time;
                        }
                        else
                        {
                            machine.MachineData.CycleTime = machine.MachineData.SecondCycleTime;
                        }
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

                }
                else // Eğer bir makineden ilk defa veri alınacaksa güncel durum tablosuna kaydeder
                {
                    machineCurrentStatus = new MachineCurrentStatus
                    {
                        MachineID = machine.MachineID,
                        ProducedItems = machine.MachineData.CurrentProduction,
                        CycleTime = machine.MachineData.CycleTime,
                        OKProductCount = machine.MachineData.OKProductCount,
                        NGProductCount = machine.MachineData.NGProductCount,
                        AverageCycleTime = 0,
                        OKRatio = machine.MachineData.OKRatio,
                        Timestamp = DateTime.Now,
                        StatusTypeID = 5,
                        CurrentDailyNGProduction = 0,
                        CurrentDailyOKProduction = 0,
                        CurrentDailyProduction = 0,
                    };

                    context.MachineCurrentStatus.Add(machineCurrentStatus);
                }
                context.SaveChanges();
            }
        }

        public void WriteDataBaseDisconnection(MachineInfo machine) 
        {
            using (var context = new ernamas_dijitalEntities())
            {
                context.Database.CommandTimeout = 5; // ...saniye bağlantı kurulması için bekleyecek
                int retryCount = 3; // Eğer bir SQL bağlantı sorunu olursa 3 kere yeniden deneme (test edilmek üzere yazılmıştır)
                for (int i = 0; i < retryCount; i++)
                {
                    try
                    {
                        var machineCurrentStatus = context.MachineCurrentStatus.FirstOrDefault(l => l.MachineID == machine.MachineID);
                        if (machineCurrentStatus != null)
                        {
                            machineCurrentStatus.Timestamp = DateTime.Now;
                            machineCurrentStatus.StatusTypeID = 3; // Disconnect
                        }
                        context.SaveChanges();
                        break; // Başarılı olduysa döngüden çık
                    }
                    catch
                    {
                        if (i == retryCount - 1) throw; // Son denemede hala hata varsa fırlat
                        System.Threading.Thread.Sleep(50); // Bekle ve yeniden dene
                    }
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
                LogText($"{modbusClient.IPAddress}\t Failed to disconnect: {ex.Message}");
            }

            try
            {
                modbusClient.Connect();
                LogText($"{modbusClient.IPAddress}\t Modbus client reconnected.");
            }
            catch (Exception ex)
            {
                LogText($"{modbusClient.IPAddress}\t Failed to reconnect: {ex.Message}");
            }
        }

        public void ReadMachineData()
        {
            using (var context = new ernamas_dijitalEntities())
            {
                var machines = context.Machines.ToList();

                foreach (var machine in machines)
                {
                    if (machine.State == 1) // Eğer makina aktifse
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
        }

        public void AllMachinPingTest()
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

        public void OneMachinePingTest(string ipAdress)
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
            // Özetleme işlemleri her tetikleme anında yapılacak
            PerformDailySummary();
            PerformDailyNGSummary();

            // Mevcut zamanı al
            DateTime now = DateTime.Now;

            // Saat 23:48 ile 23:59 arasında mı kontrol et
            if (now.Hour == 23 && now.Minute >= 48 && now.Minute <= 59)
            {
                MachineCurrentStatusReset(); // Bu zaman aralığında çalışacak fonksiyon
            }
        }

        private void MachineCurrentStatusReset()
        {
            using (var context = new ernamas_dijitalEntities())
            {
                // Tüm MachineCurrentStatus kayıtlarını getir
                var machineStatuses = context.MachineCurrentStatus.ToList();

                // Her bir kayıttaki ilgili sütunları sıfırla
                foreach (var status in machineStatuses)
                {
                    status.CurrentDailyProduction = 0;
                    status.CurrentDailyOKProduction = 0;
                    status.CurrentDailyNGProduction = 0;
                }

                // Değişiklikleri kaydet
                context.SaveChanges();
            }
        }


        private void PerformDailySummary()
        {
            try
            {
                using (var context = new ernamas_dijitalEntities())
                {
                    DateTime today = DateTime.Today;

                    // Bugüne ait tüm MachineHistoryStatus verilerini sil
                    var existingHistoryRecords = context.MachineHistoryStatus.Where(h => h.DateDay == today);
                    context.MachineHistoryStatus.RemoveRange(existingHistoryRecords);

                    // Bugüne ait tüm MachineLossQualities verilerini sil
                    var existingLossRecords = context.MachineLossQualities.Where(l => l.LossDate == today && l.employeeId == 0);
                    context.MachineLossQualities.RemoveRange(existingLossRecords);

                    // MachineCurrentStatus tablosundaki verileri al
                    var currentStatusList = context.MachineCurrentStatus.ToList();

                    // Her bir kayıt için MachineHistoryStatus ve hurda ekleme işlemi yap
                    foreach (var currentStatus in currentStatusList)
                    {
                        double averageCycleTime = GetAverageCycleTimeForToday(currentStatus.MachineID);

                        // MachineHistoryStatus tablosuna ekleme yap
                        var historyStatus = new MachineHistoryStatus
                        {
                            DateDay = today,
                            MachineID = currentStatus.MachineID,
                            AverageCycleTime = averageCycleTime,
                            ProducedItems = currentStatus.CurrentDailyProduction,
                            OKProductCount = currentStatus.CurrentDailyOKProduction,
                            NGProductCount = currentStatus.CurrentDailyNGProduction,
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
                                MachineFaultTypeID = 4,
                                LossQuantity = currentStatus.NGProductCount,
                                LossDate = today,
                                employeeId = 0,
                            };

                            context.MachineLossQualities.Add(lossQuality);
                        }
                    }

                    // Veritabanında değişiklikleri kaydet
                    context.SaveChanges();

                    LogText("Günlük özetleme işlemi başarıyla gerçekleştirildi.");
                }
            }
            catch (Exception ex)
            {
                LogText($"Özetleme işlemi sırasında bir hata oluştu: {ex.Message}");
            }
        }

        private void PerformDailyNGSummary() 
        {
            foreach (var machine in MachineList.Values)
            {
                using (var context = new ernamas_dijitalEntities())
                {
                    // Günün tarihini al
                    DateTime today = DateTime.Today;

                    // Gün sonu tablosundan ilgili makine için gün sonu kaydını al
                    var machineHistoryStatus = context.MachineHistoryStatus
                        .FirstOrDefault(m => m.MachineID == machine.MachineID && m.DateDay == today);

                    if (machineHistoryStatus != null)
                    {
                        // Bugün eklenen hurda kayıtlarını al
                        var todayLosses = context.MachineLossQualities
                            .Where(l => l.MachineID == machine.MachineID && l.LossDate == today)
                            .Sum(l => l.LossQuantity);

                        // Mevcut NGProductCount'u güncelleyerek ekle
                        if (todayLosses != null)
                        {
                            machineHistoryStatus.NGProductCount += todayLosses;
                        }

                        // Değişiklikleri veritabanına kaydet
                        context.SaveChanges();
                    }

                    // Veritabanında değişiklikleri kaydet
                    context.SaveChanges();
                }
            }
        }

        private static void SaveNewCycleTimeData(int? machineID, DateTime productionDate, int? productionCount, float cycleTime)
        {
            using (var context = new ernamas_dijitalEntities())
            {
                var machineCurrentStatus = context.MachineCurrentStatus.FirstOrDefault(l => l.MachineID == machineID); // Burada kalındı

                var newLog = new MachineProductionDatas
                {
                    MachineID = Convert.ToInt32(machineID),
                    ProductionDate = productionDate,
                    ProductionCount = Convert.ToInt32(productionCount),
                    CycleTime = cycleTime,
                    CalculatedCycleTime = cycleTime
                };

                context.MachineProductionDatas.Add(newLog);
                context.SaveChanges();
            }
        }
        public void LogText(string text)
        {
            using (StreamWriter logWriter = new StreamWriter(LogFilePath, true))
            {
                logWriter.AutoFlush = true;

                string logMessage = $"{DateTime.Now}\t {text}";
                logWriter.WriteLine(logMessage);
            }
        }

        private void UpdateUI(MachineInfo machine)
        {
            if (textBox1.InvokeRequired || textBox2.InvokeRequired)
            {
                textBox1.Invoke((MethodInvoker)delegate
                {
                    textBox1.Text = machine.IPAddress.ToString();
                });

                textBox2.Invoke((MethodInvoker)delegate
                {
                    textBox2.Text = machine.MachineName.ToString();
                });
            }
            else
            {
                textBox1.Text = machine.IPAddress.ToString();
                textBox2.Text = machine.MachineName.ToString();
            }
        }

        public double GetAverageCycleTimeForToday(int? machineId)
        {
            DateTime today = DateTime.Today;
            DateTime tomorrow = today.AddDays(1);

            using (var context = new ernamas_dijitalEntities())
            {
                var averageCycleTime = context.MachineProductionDatas
                    .Where(x => x.MachineID == machineId
                                && x.ProductionDate >= today
                                && x.ProductionDate < tomorrow)
                    .Average(x => (double?)x.CycleTime) ?? 0; // Boş sonuçlar için 0 döner

                return averageCycleTime;
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
        // MachineData'yı bir özellik olarak ekleyelim
        public MachineData MachineData { get; set; }

        // Yapıcı metot
        public MachineInfo()
        {
            // MachineData nesnesini başlatıyoruz
            MachineData = new MachineData();
        }
    }
    public class MachineData
    {
        public int CurrentProduction { get; set; }
        public int OKProductCount { get; set; }
        public int NGProductCount { get; set; }
        public float OKRatio { get; set; }
        public float CycleTime { get; set; }
        public float SecondCycleTime { get; set; }

        public bool Auto { get; set; }
        public bool Run { get; set; }
        public bool Ready { get; set; }
        public bool Fault { get; set; }
    }
}
