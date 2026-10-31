Console.WriteLine("DiagTest console app started OK.");
File.AppendAllText(@"C:\ProgramData\LabMonitoring\Agent\logs\diagtest.txt", $"{DateTime.Now}: console DiagTest ran, args=[{string.Join(' ', args)}]\r\n");
Console.WriteLine("DiagTest console app finished OK.");
