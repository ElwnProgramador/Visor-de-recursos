using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Security.Principal;
using System.Threading;
using System.Collections.Generic;

/// Clase principal que implementa un monitor de recursos del sistema en tiempo real
/// Monitorea CPU, RAM, disco y red, mostrando estadísticas y alertas
/// 
class ResourceMonitor
{
    // Contadores de rendimiento para obtener métricas del sistema
    static PerformanceCounter cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
    static PerformanceCounter ramCounter = new PerformanceCounter("Memory", "% Committed Bytes In Use");
    static PerformanceCounter diskCounter = new PerformanceCounter("PhysicalDisk", "% Disk Time", "_Total");
    static PerformanceCounter ramAvailableCounter = new PerformanceCounter("Memory", "Available MBytes");
    static PerformanceCounter diskReadCounter = new PerformanceCounter("PhysicalDisk", "Disk Read Bytes/sec", "_Total");
    static PerformanceCounter diskWriteCounter = new PerformanceCounter("PhysicalDisk", "Disk Write Bytes/sec", "_Total");
    static PerformanceCounter networkSentCounter;
    static PerformanceCounter networkReceivedCounter;

    // Variables para estadísticas y seguimiento del tiempo
    static Stopwatch runtime = new Stopwatch();
    static float maxCpu = 0, maxRam = 0, maxDisk = 0, maxDiskRead = 0, maxDiskWrite = 0;
    static float avgCpu = 0, avgRam = 0, avgDisk = 0, avgDiskRead = 0, avgDiskWrite = 0;
    static int sampleCount = 0;
    static int refreshRate = 2000; // Intervalo de actualización en milisegundos
    static bool enableLogging = true;
    static string logFilePath = "resource_monitor_log.csv";
    static bool isAdministrator = false;
    static bool networkMonitoringEnabled = true;
    static int cpuAlertThreshold = 80; // Umbral para alertas de CPU (%)
    static int ramAlertThreshold = 80; // Umbral para alertas de RAM (%)
    static int diskAlertThreshold = 80; // Umbral para alertas de disco (%)
    static bool monitoring = true;

    // Líneas base para posicionamiento en la consola
    static int processListStartLine = 20;
    static int alertStartLine;

    // Historial de métricas para graficación
    static List<float> cpuHistory = new List<float>();
    static List<float> ramHistory = new List<float>();
    static List<float> diskHistory = new List<float>();

    // Variable para controlar modo simplificado de visualización
    static bool simplifiedMode = false;

 
    /// Método auxiliar para establecer la posición del cursor de manera segura
    /// Evita excepciones si la posición está fuera del buffer

    static void SafeSetCursorPosition(int left, int top)
    {
        try
        {
            // Asegurarse de que la posición esté dentro de los límites del buffer
            if (top >= 0 && top < Console.BufferHeight && left >= 0 && left < Console.BufferWidth)
            {
                Console.SetCursorPosition(left, top);
            }
        }
        catch
        {
            // Ignorar silenciosamente errores de posicionamiento
        }
    }


    /// Inicializa la pantalla de la consola y configura sus dimensiones

    static void InitializeScreen()
    {
        try
        {
            Console.Clear();
            // Asegurarse de que la consola tenga un tamaño adecuado
            try
            {
                // Configurar dimensiones mínimas del buffer
                int minHeight = 60; // Ajustar según necesidades
                Console.BufferHeight = Math.Max(minHeight, Console.WindowHeight);
                Console.WindowHeight = Math.Min(40, Console.BufferHeight);
            }
            catch
            {
                // Algunos entornos no permiten cambiar el tamaño, usar modo simplificado
                Console.WriteLine("Advertencia: No se puede cambiar el tamaño del buffer de la consola.");
                Console.WriteLine("Cambiando a modo simplificado con menos elementos visuales.");
                Thread.Sleep(2000);
                simplifiedMode = true;
            }

            // Calcular valores apropiados basados en el tamaño actual del buffer
            int bufferHeight = Console.BufferHeight;
            processListStartLine = Math.Min(20, bufferHeight - 20);
            alertStartLine = Math.Min(processListStartLine + 8, bufferHeight - 5);

            // Dibujar encabezado inicial
            Console.WriteLine("=================================================");
            Console.WriteLine("             MONITOREO DEL SISTEMA               ");
            Console.WriteLine("=================================================");
            Console.WriteLine("Tiempo de ejecución: 00:00:00");
            Console.WriteLine("Última actualización: " + DateTime.Now);
            Console.WriteLine("=================================================");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error al inicializar la pantalla: {ex.Message}");
            monitoring = false;
        }
    }

    /// <summary>
    /// Actualiza la pantalla con las métricas actuales del sistema
    /// </summary>
    /// <param name="cpu">Porcentaje de uso de CPU</param>
    /// <param name="ram">Porcentaje de uso de RAM</param>
    /// <param name="disk">Porcentaje de uso de disco</param>
    /// <param name="ramAvailable">RAM disponible en MB</param>
    /// <param name="networkSent">Datos de red enviados (KB/s)</param>
    /// <param name="networkReceived">Datos de red recibidos (KB/s)</param>
    /// <param name="diskRead">Velocidad de lectura de disco (KB/s)</param>
    /// <param name="diskWrite">Velocidad de escritura de disco (KB/s)</param>
    static void UpdateScreen(float cpu, float ram, float disk, float ramAvailable, float networkSent, float networkReceived, float diskRead, float diskWrite)
    {
        try
        {
            // SOLUCIÓN: Limpiar toda la pantalla y redibujarla completamente en cada actualización
            Console.Clear();

            // --- Encabezado ---
            Console.WriteLine("=================================================");
            Console.WriteLine("             MONITOREO DEL SISTEMA               ");
            Console.WriteLine("=================================================");
            Console.WriteLine($"Tiempo de ejecución: {runtime.Elapsed}");
            Console.WriteLine($"Última actualización: {DateTime.Now}");
            Console.WriteLine("=================================================");

            // --- Métricas principales ---
            // Usar una constante para la alineación de valores
            const string valueFormat = "{0,8:F1}";
            const string valueFormatInt = "{0,8:F0}";

            // Mostrar barras de progreso para métricas principales
            DisplayProgressBar("CPU", cpu, 100, ConsoleColor.Green);
            DisplayProgressBar("RAM", ram, 100, ConsoleColor.Yellow);
            DisplayProgressBar("DISK", disk, 100, ConsoleColor.Cyan);

            // Si estamos en modo simplificado, mostrar menos elementos
            if (!simplifiedMode)
            {
                // Mostrar métricas detalladas
                DisplayMetric("RAM Disponible", ramAvailable, ConsoleColor.Magenta, valueFormatInt);
                DisplayMetric("Red", networkSent, networkReceived, ConsoleColor.Blue, valueFormat);
                DisplayMetric("Disco", diskRead, diskWrite, ConsoleColor.DarkYellow, valueFormat);
                Console.WriteLine("-------------------------------------------------");

                // Mostrar valores máximos
                DisplayMetric("Máx CPU", maxCpu, ConsoleColor.Green, valueFormat);
                DisplayMetric("Máx RAM", maxRam, ConsoleColor.Yellow, valueFormat);
                DisplayMetric("Máx DISK", maxDisk, ConsoleColor.Cyan, valueFormat);
                DisplayMetric("Máx Disco", maxDiskRead, maxDiskWrite, ConsoleColor.DarkYellow, valueFormat);
                Console.WriteLine("-------------------------------------------------");

                // Mostrar valores promedio
                DisplayMetric("Prom CPU", avgCpu, ConsoleColor.Green, valueFormat);
                DisplayMetric("Prom RAM", avgRam, ConsoleColor.Yellow, valueFormat);
                DisplayMetric("Prom DISK", avgDisk, ConsoleColor.Cyan, valueFormat);
                DisplayMetric("Prom Disco", avgDiskRead, avgDiskWrite, ConsoleColor.DarkYellow, valueFormat);
                Console.WriteLine("=================================================");

                // Actualizar historial para gráficos
                cpuHistory.Add(cpu);
                ramHistory.Add(ram);
                diskHistory.Add(disk);

                // Mantener solo los últimos 30 valores para los gráficos
                if (cpuHistory.Count > 30) cpuHistory.RemoveAt(0);
                if (ramHistory.Count > .30) ramHistory.RemoveAt(0);
                if (diskHistory.Count > 30) diskHistory.RemoveAt(0);

                // Solo mostrar gráficos si hay suficiente espacio en el buffer
                if (Console.BufferHeight > 35)
                {
                    DisplayGraph("Historial CPU", cpuHistory.ToArray(), ConsoleColor.Green);
                }
            }
            else
            {
                // En modo simplificado, mostrar solo lo básico
                Console.WriteLine("=================================================");
                processListStartLine = Console.CursorTop;
                alertStartLine = processListStartLine + 6;
            }

            // Actualizar lista de procesos y alertas después de redefinir las líneas base
            DisplayProcesses();
            DisplayAlerts(cpu, ram, disk);
        }
        catch (Exception ex)
        {
            // Si algo falla, reiniciar la pantalla
            try
            {
                Console.Clear();
                Console.WriteLine($"Error al actualizar la pantalla: {ex.Message}");
                Console.WriteLine("Reiniciando pantalla en 2 segundos...");
                Thread.Sleep(2000);
                InitializeScreen();
            }
            catch
            {
                // Último recurso si todo falla
                monitoring = false;
            }
        }
    }

    /// <summary>
    /// Limpia la línea actual de la consola
    /// </summary>
    static void ClearCurrentConsoleLine()
    {
        try
        {
            int currentLineCursor = Console.CursorTop;
            Console.Write(new string(' ', Console.WindowWidth - 1));
            Console.SetCursorPosition(0, currentLineCursor);
        }
        catch
        {
            // Ignorar errores de limpieza de línea
        }
    }

    /// <summary>
    /// Muestra una barra de progreso para visualizar un valor porcentual
    /// </summary>
    /// <param name="label">Etiqueta descriptiva</param>
    /// <param name="value">Valor actual</param>
    /// <param name="maxValue">Valor máximo posible</param>
    /// <param name="color">Color para la barra</param>
    static void DisplayProgressBar(string label, float value, float maxValue, ConsoleColor color)
    {
        try
        {
            int barWidth = Math.Min(30, Console.WindowWidth - 25); // Ajustar ancho de barra según ventana
            int filledWidth = (int)(value / maxValue * barWidth);

            // Asegurar que filledWidth esté en rango válido
            filledWidth = Math.Max(0, Math.Min(filledWidth, barWidth));

            // Crear una barra usando caracteres de bloque completo y vacío
            string bar = new string('█', filledWidth) + new string('░', barWidth - filledWidth);

            Console.ForegroundColor = color;
            Console.WriteLine($"{label,-15}: [{bar}] {value,6:F1}%");
            Console.ResetColor();
        }
        catch
        {
            // Ignorar errores de visualización
        }
    }

    /// <summary>
    /// Muestra una métrica simple con su valor
    /// </summary>
    /// <param name="label">Etiqueta descriptiva</param>
    /// <param name="value">Valor a mostrar</param>
    /// <param name="color">Color del texto</param>
    /// <param name="format">Formato de visualización</param>
    static void DisplayMetric(string label, float value, ConsoleColor color, string format)
    {
        try
        {
            Console.ForegroundColor = color;
            Console.WriteLine($"{label,-15}: {string.Format(format, value)}%");
            Console.ResetColor();
        }
        catch
        {
            // Ignorar errores de visualización
        }
    }

    /// <summary>
    /// Muestra una métrica compuesta de dos valores (lectura/escritura)
    /// </summary>
    /// <param name="label">Etiqueta descriptiva</param>
    /// <param name="value1">Primer valor (lectura)</param>
    /// <param name="value2">Segundo valor (escritura)</param>
    /// <param name="color">Color del texto</param>
    /// <param name="format">Formato de visualización</param>
    static void DisplayMetric(string label, float value1, float value2, ConsoleColor color, string format)
    {
        try
        {
            Console.ForegroundColor = color;
            Console.WriteLine($"{label,-15}: L: {string.Format(format, value1)} KB/s | E: {string.Format(format, value2)} KB/s");
            Console.ResetColor();
        }
        catch
        {
            // Ignorar errores de visualización
        }
    }

    /// <summary>
    /// Muestra un gráfico de tendencia basado en valores históricos
    /// </summary>
    /// <param name="label">Etiqueta del gráfico</param>
    /// <param name="values">Array de valores históricos</param>
    /// <param name="color">Color del gráfico</param>
    static void DisplayGraph(string label, float[] values, ConsoleColor color)
    {
        try
        {
            // Determinar altura disponible para el gráfico
            int graphHeight = Math.Min(8, Console.BufferHeight - Console.CursorTop - 10);
            if (graphHeight <= 0 || values.Length == 0)
                return;

            // Encontrar el valor máximo para escalar el gráfico
            float maxValue = values.Length > 0 ? values.Max() : 100;
            if (maxValue <= 0) maxValue = 100; // Evitar división por cero

            Console.ForegroundColor = color;
            Console.WriteLine($"{label}:");

            // Dibujar el gráfico línea por línea, de arriba a abajo
            for (int i = graphHeight; i >= 0; i--)
            {
                foreach (var value in values)
                {
                    // Determinar si el punto actual debe tener un carácter según su altura relativa
                    if (value / maxValue * graphHeight >= i)
                    {
                        Console.Write("█");
                    }
                    else
                    {
                        Console.Write(" ");
                    }
                }
                Console.WriteLine();
            }
            Console.ResetColor();
        }
        catch
        {
            // Ignorar errores de visualización
        }
    }

    /// <summary>
    /// Muestra los 5 procesos que consumen más memoria
    /// </summary>
    static void DisplayProcesses()
    {
        try
        {
            Console.WriteLine("Procesos principales:");
            Console.WriteLine("-------------------------------------------------");

            // Obtener todos los procesos y ordenarlos por uso de memoria
            Process[] processes = Process.GetProcesses();
            var topProcesses = processes
                .Where(p => !string.IsNullOrEmpty(p.ProcessName))
                .OrderByDescending(p => p.WorkingSet64)
                .Take(5);

            // Mostrar los 5 procesos que más memoria consumen
            foreach (var process in topProcesses)
            {
                try
                {
                    string name = process.ProcessName;
                    long memoryMB = process.WorkingSet64 / (1024 * 1024);

                    // Garantizar que el nombre del proceso se trunca/rellena a 25 caracteres
                    name = name.Length > 25 ? name.Substring(0, 25) : name.PadRight(25);
                    Console.WriteLine($"{name} | RAM: {memoryMB,8:N0} MB");
                }
                catch { }
            }

            // Actualizar la línea de alertas después de los procesos
            alertStartLine = Console.CursorTop + 1;
        }
        catch
        {
            // Ignorar errores
        }
    }

    /// <summary>
    /// Muestra alertas cuando los recursos superan los umbrales definidos
    /// </summary>
    /// <param name="cpu">Porcentaje de uso de CPU</param>
    /// <param name="ram">Porcentaje de uso de RAM</param>
    /// <param name="disk">Porcentaje de uso de disco</param>
    static void DisplayAlerts(float cpu, float ram, float disk)
    {
        try
        {
            bool alertsPresent = false;

            // Verificar y mostrar alerta de CPU alta
            if (cpu > cpuAlertThreshold)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"ALERTA: Uso de CPU alto ({cpu:F1}%)");
                Console.ResetColor();
                try { Console.Beep(); } catch { } // Alerta sonora
                alertsPresent = true;
            }

            // Verificar y mostrar alerta de RAM alta
            if (ram > ramAlertThreshold)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"ALERTA: Uso de RAM alto ({ram:F1}%)");
                Console.ResetColor();
                try { Console.Beep(); } catch { } // Alerta sonora
                alertsPresent = true;
            }

            // Verificar y mostrar alerta de disco alto
            if (disk > diskAlertThreshold)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"ALERTA: Uso de Disco alto ({disk:F1}%)");
                Console.ResetColor();
                try { Console.Beep(); } catch { } // Alerta sonora
                alertsPresent = true;
            }

            // Si no hay alertas, dejar un espacio
            if (!alertsPresent)
            {
                Console.WriteLine();
            }

            // Información de control
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("[ESC] Salir  [S] Cambiar modo");
            Console.ResetColor();
        }
        catch
        {
            // Ignorar errores de posicionamiento
        }
    }

    /// <summary>
    /// Registra los datos de rendimiento en un archivo CSV
    /// </summary>
    /// <param name="cpu">Porcentaje de uso de CPU</param>
    /// <param name="ram">Porcentaje de uso de RAM</param>
    /// <param name="disk">Porcentaje de uso de disco</param>
    /// <param name="ramAvailable">RAM disponible en MB</param>
    /// <param name="networkSent">Datos de red enviados (KB/s)</param>
    /// <param name="networkReceived">Datos de red recibidos (KB/s)</param>
    /// <param name="diskRead">Velocidad de lectura de disco (KB/s)</param>
    /// <param name="diskWrite">Velocidad de escritura de disco (KB/s)</param>
    static void LogData(float cpu, float ram, float disk, float ramAvailable, float networkSent, float networkReceived, float diskRead, float diskWrite)
    {
        if (!enableLogging) return;

        try
        {
            bool fileExists = File.Exists(logFilePath);

            using (StreamWriter writer = new StreamWriter(logFilePath, true))
            {
                // Si el archivo no existe, escribir el encabezado
                if (!fileExists)
                {
                    writer.WriteLine("Timestamp,CPU(%),RAM(%),Disk(%),RAM Available(MB),Network Sent(KB/s),Network Received(KB/s),Disk Read(KB/s),Disk Write(KB/s)");
                }

                // Escribir los datos con formato CSV
                writer.WriteLine($"{DateTime.Now},{cpu:F1},{ram:F1},{disk:F1},{ramAvailable:F0},{networkSent:F1},{networkReceived:F1},{diskRead:F1},{diskWrite:F1}");
            }
        }
        catch (Exception ex)
        {
            try
            {
                // Mostrar error y deshabilitar logging para futuros intentos
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"Error al guardar log: {ex.Message.Substring(0, Math.Min(ex.Message.Length, 45))}");
                Console.ResetColor();
                enableLogging = false;
            }
            catch { }
        }
    }

    /// <summary>
    /// Método principal para monitorear recursos del sistema
    /// Maneja el bucle principal y la captura de métricas
    /// </summary>
    static void MonitorResources()
    {
        try
        {
            runtime.Start();
            InitializeScreen();
            cpuCounter.NextValue(); // Primera llamada para inicializar correctamente
            Thread.Sleep(500);

            while (monitoring)
            {
                // Verificar si hay teclas presionadas
                if (Console.KeyAvailable)
                {
                    ConsoleKeyInfo key = Console.ReadKey(true);
                    if (key.Key == ConsoleKey.Escape)
                    {
                        monitoring = false;
                        break;
                    }
                    else if (key.Key == ConsoleKey.S)
                    {
                        // Alternar modo simplificado con tecla S
                        simplifiedMode = !simplifiedMode;
                        Console.Clear();
                    }
                }

                // Capturar valores actuales de rendimiento
                float cpu = cpuCounter.NextValue();
                float ram = ramCounter.NextValue();
                float disk = diskCounter.NextValue();
                float ramAvailable = ramAvailableCounter.NextValue();
                float networkSent = networkMonitoringEnabled ? networkSentCounter.NextValue() / 1024 : 0;
                float networkReceived = networkMonitoringEnabled ? networkReceivedCounter.NextValue() / 1024 : 0;
                float diskRead = diskReadCounter.NextValue() / 1024;
                float diskWrite = diskWriteCounter.NextValue() / 1024;

                // Actualizar estadísticas (máximos)
                maxCpu = Math.Max(maxCpu, cpu);
                maxRam = Math.Max(maxRam, ram);
                maxDisk = Math.Max(maxDisk, disk);
                maxDiskRead = Math.Max(maxDiskRead, diskRead);
                maxDiskWrite = Math.Max(maxDiskWrite, diskWrite);

                // Actualizar estadísticas (promedios)
                sampleCount++;
                avgCpu = ((avgCpu * (sampleCount - 1)) + cpu) / sampleCount;
                avgRam = ((avgRam * (sampleCount - 1)) + ram) / sampleCount;
                avgDisk = ((avgDisk * (sampleCount - 1)) + disk) / sampleCount;
                avgDiskRead = ((avgDiskRead * (sampleCount - 1)) + diskRead) / sampleCount;
                avgDiskWrite = ((avgDiskWrite * (sampleCount - 1)) + diskWrite) / sampleCount;

                // Actualizar pantalla y registrar datos
                UpdateScreen(cpu, ram, disk, ramAvailable, networkSent, networkReceived, diskRead, diskWrite);
                LogData(cpu, ram, disk, ramAvailable, networkSent, networkReceived, diskRead, diskWrite);

                // Esperar hasta la próxima actualización
                Thread.Sleep(refreshRate);
            }
        }
        catch (Exception ex)
        {
            Console.Clear();
            Console.WriteLine($"Error durante el monitoreo: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
        }
        finally
        {
            Console.Clear();
            Console.WriteLine("Monitoreo detenido. Presione cualquier tecla para salir.");
            Console.ReadKey(true);
        }
    }

    /// <summary>
    /// Punto de entrada de la aplicación
    /// </summary>
    /// <param name="args">Argumentos de línea de comandos (no utilizados)</param>
    static void Main(string[] args)
    {
        try
        {
            // Verificar si la aplicación se ejecuta con permisos de administrador
            isAdministrator = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

            if (!isAdministrator)
            {
                // Si no tiene permisos de administrador, mostrar advertencia y salir
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Advertencia: Esta aplicación debe ejecutarse con permisos de administrador para funcionar correctamente.");
                Console.ResetColor();
                Console.WriteLine("Presione cualquier tecla para salir.");
                Console.ReadKey(true);
                return;
            }

            Console.WriteLine("Iniciando monitoreo de recursos del sistema...");
            Console.WriteLine("Presione ESC para salir o S para alternar modo simplificado.");
            Console.WriteLine("Ajustando el tamaño de la ventana...");

            // Inicializar contadores de red
            networkMonitoringEnabled = InitializeNetworkCounters();
            // Iniciar el monitoreo
            MonitorResources();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error al iniciar la aplicación: {ex.Message}");
            Console.WriteLine("Presione cualquier tecla para salir.");
            Console.ReadKey(true);
        }
    }

    /// <summary>
    /// Inicializa los contadores de rendimiento de red
    /// </summary>
    /// <returns>True si los contadores se inicializaron correctamente, False en caso contrario</returns>
    static bool InitializeNetworkCounters()
    {
        try
        {
            // Encontrar una interfaz de red activa para monitorear
            string networkInterface = NetworkInterface.GetAllNetworkInterfaces()
                .FirstOrDefault(ni => ni.OperationalStatus == OperationalStatus.Up &&
                                      (ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ||
                                       ni.NetworkInterfaceType == NetworkInterfaceType.Ethernet) &&
                                      ni.GetIPv4Statistics().BytesSent > 0)?.Description;

            if (!string.IsNullOrEmpty(networkInterface))
            {
                // Inicializar contadores para la interfaz encontrada
                networkSentCounter = new PerformanceCounter("Network Interface", "Bytes Sent/sec", networkInterface);
                networkReceivedCounter = new PerformanceCounter("Network Interface", "Bytes Received/sec", networkInterface);
                return true;
            }
            else
            {
                Console.WriteLine("No se encontró una interfaz de red válida.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error al inicializar los contadores de red: {ex.Message}");
        }
        return false;
    }
}