using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading;
using System.Security.Principal;

class ResourceMonitor
{
    // Contadores de rendimiento existentes
    static PerformanceCounter cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
    static PerformanceCounter ramCounter = new PerformanceCounter("Memory", "% Committed Bytes In Use");
    static PerformanceCounter diskCounter = new PerformanceCounter("PhysicalDisk", "% Disk Time", "_Total");

    // Nuevos contadores de rendimiento
    static PerformanceCounter ramAvailableCounter = new PerformanceCounter("Memory", "Available MBytes");
    static PerformanceCounter diskReadCounter = new PerformanceCounter("PhysicalDisk", "Disk Read Bytes/sec", "_Total");
    static PerformanceCounter diskWriteCounter = new PerformanceCounter("PhysicalDisk", "Disk Write Bytes/sec", "_Total");
    static PerformanceCounter networkSentCounter;
    static PerformanceCounter networkReceivedCounter;

    // Variables para el tiempo de ejecución y valores máximos
    static Stopwatch runtime = new Stopwatch();
    static float maxCpu = 0, maxRam = 0, maxDisk = 0;
    static float avgCpu = 0, avgRam = 0, avgDisk = 0;
    static int sampleCount = 0;

    // Lista para almacenar datos históricos
    static List<ResourceData> historicalData = new List<ResourceData>();

    // Configuración
    static int refreshRate = 2000; // ms
    static bool enableLogging = true;
    static string logFilePath = "resource_monitor_log.csv";
    static int maxDataPoints = 30; // Número de puntos de datos a mantener para gráficos históricos
    static bool isAdministrator = false; // Verificar si se ejecuta como administrador
    static bool networkMonitoringEnabled = true; // Bandera para habilitar/deshabilitar monitoreo de red
    static bool isFirstRun = true; // Para determinar si es la primera ejecución

    // Posiciones de líneas para actualización dinámica
    static int lineTimerPosition = 4;
    static int lineCpuPosition = 7;
    static int lineRamPosition = 8;
    static int lineDiskPosition = 9;
    static int lineRamAvailablePosition = 10;
    static int lineNetworkPosition = 11;
    static int lineMaxValuesPosition = 14;
    static int lineAvgValuesPosition = 15;
    static int lineStatusPosition = 18;

    // Clase para almacenar datos de un punto en el tiempo
    class ResourceData
    {
        public DateTime Timestamp { get; set; }
        public float CpuUsage { get; set; }
        public float RamUsage { get; set; }
        public float DiskUsage { get; set; }
        public float NetworkSent { get; set; }
        public float NetworkReceived { get; set; }
    }

    // Método para recalcular posiciones de líneas basadas en el tamaño de la consola
    static void RecalculateLinePositions()
    {
        int availableLines = Console.BufferHeight - 2; // Menos un margen

        // Usar versión compacta si la consola es pequeña
        bool reducedMode = availableLines < 30;

        if (reducedMode)
        {
            // Versión compacta con menos espacio entre secciones
            lineTimerPosition = 2;
            lineCpuPosition = 4;
            lineRamPosition = 5;
            lineDiskPosition = 6;
            lineRamAvailablePosition = 7;
            lineNetworkPosition = 8;
            lineMaxValuesPosition = 10;
            lineAvgValuesPosition = 11;
            lineStatusPosition = 13;
        }
        else
        {
            // Configuración normal
            lineTimerPosition = 4;
            lineCpuPosition = 7;
            lineRamPosition = 8;
            lineDiskPosition = 9;
            lineRamAvailablePosition = 10;
            lineNetworkPosition = 11;
            lineMaxValuesPosition = 14;
            lineAvgValuesPosition = 15;
            lineStatusPosition = 18;
        }
    }

    // Método para inicializar la pantalla una sola vez
    static void InitializeScreen()
    {
        // Asegurar que el buffer tenga suficiente tamaño
        try
        {
            if (Console.BufferHeight < 40) // Aseguramos espacio para todas las líneas
            {
                Console.BufferHeight = 40;
            }
        }
        catch
        {
            // Algunos entornos no permiten cambiar el tamaño del buffer
            Console.WriteLine("No se pudo ajustar el tamaño del buffer. La visualización podría no ser óptima.");
            Thread.Sleep(2000);
        }

        // Recalcular posiciones basadas en el tamaño actual de la consola
        RecalculateLinePositions();

        Console.Clear();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("=================================================");
        Console.WriteLine("             MONITOREO DEL SISTEMA               ");
        Console.WriteLine("=================================================");
        Console.WriteLine("Tiempo de ejecución: 00:00:00");
        Console.WriteLine("Última actualización: " + DateTime.Now);

        if (!isAdministrator)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("NOTA: Ejecute como administrador para acceso completo");
            Console.ForegroundColor = ConsoleColor.Cyan;
        }

        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine("CPU:  [--------------------] 0.0%");
        Console.WriteLine("RAM:  [--------------------] 0.0%");
        Console.WriteLine("DISK: [--------------------] 0.0%");
        Console.WriteLine("RAM Disponible: 0 MB");
        Console.WriteLine("Red: 0.0 KB/s | 0.0 KB/s");

        Console.WriteLine("=================================================");
        Console.WriteLine("Máx CPU: 0.0% | Máx RAM: 0.0% | Máx DISK: 0.0%");
        Console.WriteLine("Prom CPU: 0.0% | Prom RAM: 0.0% | Prom DISK: 0.0%");
        Console.WriteLine("=================================================");

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("✓ Estado: Iniciando monitoreo...");
        Console.ResetColor();

        Console.WriteLine("\nTendencias:");
        for (int i = 0; i < 3; i++)
        {
            Console.WriteLine();
        }

        Console.WriteLine("\nProcesos principales:");
        for (int i = 0; i < 7; i++)
        {
            Console.WriteLine();
        }

        Console.WriteLine("\nPresiona cualquier tecla para detener el monitoreo...");
        if (enableLogging)
        {
            Console.WriteLine("Registro guardado en: " + Path.GetFullPath(logFilePath));
        }
    }

    // Método para actualizar una línea específica sin borrar toda la pantalla
    static void UpdateLine(int lineNumber, string text)
    {
        // Verificar que la línea está dentro de los límites del buffer
        if (lineNumber >= Console.BufferHeight)
        {
            // Si estamos fuera de límites, intentar recalcular las posiciones
            RecalculateLinePositions();

            // Si aún estamos fuera de límites, ignorar la actualización
            if (lineNumber >= Console.BufferHeight)
                return;
        }

        try
        {
            // Guardar posición actual
            int currentLeft = Console.CursorLeft;
            int currentTop = Console.CursorTop;

            // Mover a la línea deseada
            Console.SetCursorPosition(0, lineNumber);

            // Limpiar la línea
            Console.Write(new string(' ', Console.WindowWidth));
            Console.SetCursorPosition(0, lineNumber);

            // Escribir el nuevo texto
            Console.Write(text);

            // Volver a la posición original
            Console.SetCursorPosition(currentLeft, currentTop);
        }
        catch (ArgumentOutOfRangeException)
        {
            // Capturar específicamente errores de posición fuera de rango
            // No hacer nada, simplemente ignorar la actualización
        }
        catch (Exception)
        {
            // Capturar cualquier otra excepción relacionada con la consola
            // No hacer nada, simplemente ignorar la actualización
        }
    }

    static bool InitializeNetworkCounters()
    {
        try
        {
            string networkInterface = GetActiveNetworkInterface();
            if (!string.IsNullOrEmpty(networkInterface))
            {
                networkSentCounter = new PerformanceCounter("Network Interface", "Bytes Sent/sec", networkInterface);
                networkReceivedCounter = new PerformanceCounter("Network Interface", "Bytes Received/sec", networkInterface);
                return true;
            }
            return false;
        }
        catch (Exception)
        {
            return false;
        }
    }

    static string GetActiveNetworkInterface()
    {
        try
        {
            NetworkInterface[] interfaces = NetworkInterface.GetAllNetworkInterfaces();
            foreach (NetworkInterface ni in interfaces)
            {
                if (ni.OperationalStatus == OperationalStatus.Up &&
                    (ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ||
                     ni.NetworkInterfaceType == NetworkInterfaceType.Ethernet) &&
                    ni.GetIPv4Statistics().BytesSent > 0)
                {
                    return ni.Description;
                }
            }
            return interfaces.Length > 0 ? interfaces[0].Description : "";
        }
        catch
        {
            return "";
        }
    }

    static void UpdateResourceGraphs(float cpuPercent, float ramPercent, float diskPercent)
    {
        const int maxValue = 100;
        const int barLength = 20;
        string cpuBar = new string('#', (int)((cpuPercent / maxValue) * barLength)).PadRight(barLength, '-');
        string ramBar = new string('#', (int)((ramPercent / maxValue) * barLength)).PadRight(barLength, '-');
        string diskBar = new string('#', (int)((diskPercent / maxValue) * barLength)).PadRight(barLength, '-');

        // Actualizar líneas individuales
        UpdateLine(lineTimerPosition, $"Tiempo de ejecución: {runtime.Elapsed}");
        UpdateLine(lineTimerPosition + 1, $"Última actualización: {DateTime.Now}");

        // Usar colores en la barra CPU
        Console.ForegroundColor = cpuPercent > 80 ? ConsoleColor.Red : (cpuPercent > 60 ? ConsoleColor.Yellow : ConsoleColor.Green);
        UpdateLine(lineCpuPosition, $"CPU:  [{cpuBar}] {cpuPercent:F1}%");

        // Usar colores en la barra RAM
        Console.ForegroundColor = ramPercent > 80 ? ConsoleColor.Red : (ramPercent > 60 ? ConsoleColor.Yellow : ConsoleColor.Green);
        UpdateLine(lineRamPosition, $"RAM:  [{ramBar}] {ramPercent:F1}%");

        // Usar colores en la barra DISK
        Console.ForegroundColor = diskPercent > 80 ? ConsoleColor.Red : (diskPercent > 60 ? ConsoleColor.Yellow : ConsoleColor.Green);
        UpdateLine(lineDiskPosition, $"DISK: [{diskBar}] {diskPercent:F1}%");

        Console.ResetColor();

        // Información adicional de RAM
        float ramAvailable = ramAvailableCounter.NextValue();
        UpdateLine(lineRamAvailablePosition, $"RAM Disponible: {ramAvailable:F0} MB");

        // Información de red
        if (networkMonitoringEnabled)
        {
            try
            {
                float networkSent = networkSentCounter.NextValue() / 1024; // KB/s
                float networkReceived = networkReceivedCounter.NextValue() / 1024; // KB/s
                UpdateLine(lineNetworkPosition, $"Red: ↑ {networkSent:F1} KB/s | ↓ {networkReceived:F1} KB/s");
            }
            catch
            {
                UpdateLine(lineNetworkPosition, "Red: No disponible");
                networkMonitoringEnabled = false;
            }
        }
        else
        {
            UpdateLine(lineNetworkPosition, "Red: Monitoreo desactivado");
        }
    }

    static void UpdateMaxValues(float cpu, float ram, float disk)
    {
        if (cpu > maxCpu) maxCpu = cpu;
        if (ram > maxRam) maxRam = ram;
        if (disk > maxDisk) maxDisk = disk;

        // Actualizar promedios
        sampleCount++;
        avgCpu = ((avgCpu * (sampleCount - 1)) + cpu) / sampleCount;
        avgRam = ((avgRam * (sampleCount - 1)) + ram) / sampleCount;
        avgDisk = ((avgDisk * (sampleCount - 1)) + disk) / sampleCount;

        // Actualizar líneas de estadísticas
        UpdateLine(lineMaxValuesPosition, $"Máx CPU: {maxCpu:F1}% | Máx RAM: {maxRam:F1}% | Máx DISK: {maxDisk:F1}%");
        UpdateLine(lineAvgValuesPosition, $"Prom CPU: {avgCpu:F1}% | Prom RAM: {avgRam:F1}% | Prom DISK: {avgDisk:F1}%");
    }

    static void LogData(ResourceData data)
    {
        if (!enableLogging) return;

        try
        {
            bool fileExists = File.Exists(logFilePath);

            using (StreamWriter writer = new StreamWriter(logFilePath, true))
            {
                if (!fileExists)
                {
                    writer.WriteLine("Timestamp,CPU(%),RAM(%),Disk(%),Red_Enviado(KB/s),Red_Recibido(KB/s)");
                }

                writer.WriteLine($"{data.Timestamp},{data.CpuUsage:F1},{data.RamUsage:F1},{data.DiskUsage:F1},{data.NetworkSent:F1},{data.NetworkReceived:F1}");
            }
        }
        catch (Exception ex)
        {
            UpdateLine(lineStatusPosition + 2, $"Error al guardar log: {ex.Message}");
            enableLogging = false;
        }
    }

    static void UpdateTrendDisplay()
    {
        if (historicalData.Count <= 1) return;

        int trendLinePosition = lineStatusPosition + 3;

        // Verificar que las líneas de tendencias están dentro de los límites
        if (trendLinePosition + 4 >= Console.BufferHeight)
        {
            return; // Omitir la visualización de tendencias si no hay espacio
        }

        // Limpiar el área de tendencias
        for (int i = 0; i < 4; i++)
        {
            UpdateLine(trendLinePosition + i, "");
        }

        // Actualizar encabezado de tendencias
        UpdateLine(trendLinePosition, "Tendencias de los últimos " + historicalData.Count + " muestreos:");

        // Tendencia de CPU
        string cpuTrend = "CPU: " + GetTrendLineString(historicalData.Select(d => d.CpuUsage).ToList());
        UpdateLine(trendLinePosition + 1, cpuTrend);

        // Tendencia de RAM
        string ramTrend = "RAM: " + GetTrendLineString(historicalData.Select(d => d.RamUsage).ToList());
        UpdateLine(trendLinePosition + 2, ramTrend);

        // Tendencia de Disco
        string diskTrend = "DSK: " + GetTrendLineString(historicalData.Select(d => d.DiskUsage).ToList());
        UpdateLine(trendLinePosition + 3, diskTrend);
    }

    static string GetTrendLineString(List<float> values)
    {
        if (values.Count <= 1) return "";

        float min = values.Min();
        float max = values.Max() > min ? values.Max() : min + 1;

        string trendLine = "";
        for (int i = 0; i < values.Count; i++)
        {
            int height = (int)((values[i] - min) / (max - min) * 8);
            char c = "▁▂▃▄▅▆▇█"[Math.Min(height, 7)];
            trendLine += c;
        }

        return trendLine;
    }

    static void UpdateProcessDisplay()
    {
        int processLinePosition = lineStatusPosition + 8;

        // Verificar que las líneas de procesos están dentro de los límites
        if (processLinePosition + 7 >= Console.BufferHeight)
        {
            return; // Omitir la visualización de procesos si no hay espacio
        }

        // Limpiar el área de procesos
        for (int i = 0; i < 7; i++)
        {
            UpdateLine(processLinePosition + i, "");
        }

        UpdateLine(processLinePosition, "Procesos principales:");

        try
        {
            Process[] processes = Process.GetProcesses();

            // Ordenar por uso de memoria
            var topProcesses = processes
                .Where(p => !string.IsNullOrEmpty(p.ProcessName))
                .OrderByDescending(p => p.WorkingSet64)
                .Take(5);

            int line = 1;
            foreach (var process in topProcesses)
            {
                try
                {
                    string name = process.ProcessName;
                    string memory = (process.WorkingSet64 / (1024 * 1024)).ToString() + " MB";

                    // Solo intentar acceder a CPU time si tenemos privilegios
                    string cpuInfo = "";
                    if (isAdministrator)
                    {
                        try
                        {
                            TimeSpan cpuTime = process.TotalProcessorTime;
                            cpuInfo = $" | CPU: {cpuTime.TotalSeconds:F1}s";
                        }
                        catch
                        {
                            cpuInfo = " | CPU: N/A";
                        }
                    }

                    UpdateLine(processLinePosition + line, $"{name.PadRight(20)} | RAM: {memory.PadLeft(8)}{cpuInfo}");
                    line++;
                }
                catch
                {
                    // Ignorar procesos que no podemos acceder
                }
            }
        }
        catch (Exception ex)
        {
            UpdateLine(processLinePosition + 1, $"No se puede acceder a la información de procesos: {ex.Message}");
        }
    }

    static void UpdateSystemStatus(float cpu, float ram, float disk)
    {
        string statusText;
        ConsoleColor color;

        if (cpu > 90 || ram > 90 || disk > 90)
        {
            statusText = "⚠️ ALERTA CRÍTICA: Uso crítico de recursos";
            color = ConsoleColor.Red;
        }
        else if (cpu > 80 || ram > 80 || disk > 80)
        {
            statusText = "⚠️ ALERTA: Uso excesivo de recursos";
            color = ConsoleColor.Red;
        }
        else if (cpu > 60 || ram > 60 || disk > 60)
        {
            statusText = "⚠️ PRECAUCIÓN: Se debe monitorear recursos";
            color = ConsoleColor.Yellow;
        }
        else
        {
            statusText = "✓ Estado: Recursos en nivel estable";
            color = ConsoleColor.Green;
        }

        Console.ForegroundColor = color;
        UpdateLine(lineStatusPosition, statusText);
        Console.ResetColor();
    }

    static bool CheckAdminPrivileges()
    {
        try
        {
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
            {
                WindowsPrincipal principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
        }
        catch
        {
            return false;
        }
    }

    static void MonitorResources()
    {
        runtime.Start();

        // Inicializar la pantalla una sola vez
        InitializeScreen();

        // Iniciar con un valor para evitar lecturas iniciales incorrectas
        cpuCounter.NextValue();
        Thread.Sleep(500);

        while (!Console.KeyAvailable)
        {
            // Verificar si el tamaño de la consola ha cambiado y recalcular posiciones
            try
            {
                RecalculateLinePositions();
            }
            catch
            {
                // Ignorar errores de redimensionamiento de la consola
            }

            float cpu = cpuCounter.NextValue();
            float ram = ramCounter.NextValue();
            float disk = diskCounter.NextValue();
            float networkSent = 0;
            float networkReceived = 0;

            // Obtener datos de red si está habilitado
            if (networkMonitoringEnabled)
            {
                try
                {
                    networkSent = networkSentCounter.NextValue() / 1024; // KB/s
                    networkReceived = networkReceivedCounter.NextValue() / 1024; // KB/s
                }
                catch
                {
                    networkMonitoringEnabled = false;
                }
            }

            // Guardar datos históricos
            ResourceData currentData = new ResourceData
            {
                Timestamp = DateTime.Now,
                CpuUsage = cpu,
                RamUsage = ram,
                DiskUsage = disk,
                NetworkSent = networkSent,
                NetworkReceived = networkReceived
            };

            historicalData.Add(currentData);
            if (historicalData.Count > maxDataPoints)
            {
                historicalData.RemoveAt(0);
            }

            // Registrar en archivo si está habilitado
            if (enableLogging)
            {
                LogData(currentData);
            }

            // Actualizar los componentes de la pantalla individualmente
            UpdateResourceGraphs(cpu, ram, disk);
            UpdateMaxValues(cpu, ram, disk);
            UpdateSystemStatus(cpu, ram, disk);
            UpdateTrendDisplay();

            // Solo actualizar la lista de procesos cada 4 ciclos para reducir la carga
            if (sampleCount % 4 == 0 || isFirstRun)
            {
                UpdateProcessDisplay();
                isFirstRun = false;
            }

            Thread.Sleep(refreshRate);
        }

        // Mostrar mensaje de finalización
        int finalLinePosition = Math.Min(lineStatusPosition + 16, Console.BufferHeight - 1);
        UpdateLine(finalLinePosition, "Monitoreo detenido.");
    }

    static void Main(string[] args)
    {
        try
        {
            Console.WriteLine("Iniciando monitoreo de recursos del sistema...");

            // Configurar tamaño del buffer de la consola
            try
            {
                Console.BufferHeight = Math.Max(40, Console.BufferHeight);
            }
            catch
            {
                // Algunos entornos no permiten cambiar el tamaño del buffer
                Console.WriteLine("No se pudo configurar el tamaño del buffer de la consola.");
                Console.WriteLine("La visualización podría no ser óptima.");
                Thread.Sleep(2000);
            }

            // Verificar si se ejecuta como administrador
            isAdministrator = CheckAdminPrivileges();

            // Inicializar contadores de red
            networkMonitoringEnabled = InitializeNetworkCounters();

            // Crear el directorio para el log si no existe
            try
            {
                string directorio = Path.GetDirectoryName(logFilePath);
                if (!string.IsNullOrEmpty(directorio) && !Directory.Exists(directorio))
                {
                    Directory.CreateDirectory(directorio);
                }
            }
            catch
            {
                enableLogging = false;
            }

            // Iniciar monitoreo
            Thread.Sleep(1000);
            MonitorResources();
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error: {ex.Message}");
            Console.WriteLine($"Stack Trace: {ex.StackTrace}");
            Console.ResetColor();
            Console.WriteLine("Presione cualquier tecla para salir...");
            Console.ReadKey();
        }
    }
}