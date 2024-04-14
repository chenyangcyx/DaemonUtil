using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using static DaemonUtilNETv1.SettingJson;

namespace DaemonUtilNETv1
{
    internal class Program
    {
        static void Main(string[] args)
        {
            // 获取配置文件
            FileInfo programSettingFileinfo = getSettingFile(args);
            List<SettingItem> settingJson = getSetting(programSettingFileinfo.FullName);

            // 输出配置文件信息
            for (int i = 0; i < settingJson.Count; i++)
            {
                SettingItem item = settingJson[i];
                Console.WriteLine($"[任务{i + 1}] 任务名称: {item.taskName}");
                Console.WriteLine($"[任务{i + 1}] 运行目录: {item.workFolder}");
                Console.WriteLine($"[任务{i + 1}] 程序地址: {item.programName}");
                Console.WriteLine($"[任务{i + 1}] 运行参数: {item.runParameter}");
                Console.WriteLine();
            }

            // 准备运行目录
            prepareWorkFolder();

            // 构建程序运行用map
            Dictionary<SettingItem, string> taskItemKeyDict = new Dictionary<SettingItem, string>();
            Dictionary<SettingItem, FileInfo> taskItemFileDict = new Dictionary<SettingItem, FileInfo>();
            Dictionary<SettingItem, bool> taskItemRunningStatus = new Dictionary<SettingItem, bool>();
            Dictionary<SettingItem, int> taskItemPid = new Dictionary<SettingItem, int>();

            // 初始化taskItemKeyDict
            foreach (SettingItem item in settingJson)
            {
                taskItemKeyDict[item] = getTaskKey(item, settingJson, programSettingFileinfo);
                taskItemFileDict[item] = new FileInfo(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "workingFolder", taskItemKeyDict[item] + ".pid"));
            }

            while (true)
            {
                Console.WriteLine("--------------------------------------------------");
                Console.WriteLine($"检测时间: {DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}");
                List<SettingItem> toRunProgramList = new List<SettingItem>();

                // 刷新所有任务状态并且打印状态
                refreshAllTaskStatus(settingJson, taskItemFileDict, taskItemRunningStatus, taskItemPid);
                for (int i = 0; i < settingJson.Count; i++)
                {
                    SettingItem item = settingJson[i];
                    FileInfo taskFileInfo = taskItemFileDict[item];
                    bool taskRunStatus = taskItemRunningStatus[item];
                    int taskPid = taskItemPid.ContainsKey(item) ? taskItemPid[item] : -1;
                    Console.WriteLine($"任务名称: {item.taskName}");
                    Console.WriteLine($" - pid文件路径: {taskFileInfo.Name}");
                    Console.WriteLine($" - task运行状态: {taskRunStatus}");
                    Console.WriteLine($" - task运行pid: {(taskPid == -1 ? "未运行" : taskPid)}");

                    if (!taskRunStatus)
                    {
                        toRunProgramList.Add(item);
                    }
                }

                // 运行所有未运行的任务
                foreach (SettingItem item in toRunProgramList)
                {
                    Process process = runCMD_Windows(item);
                    Console.WriteLine($"- 运行程序: {item.taskName}[processName: {process.ProcessName}, pid: {process.Id}]");
                    // 将进程pid写入到指定文件
                    using (StreamWriter writer = new StreamWriter(taskItemFileDict[item].FullName, false, Encoding.UTF8))
                    {
                        writer.AutoFlush = true;
                        writer.Write(process.Id);
                    }
                }

                Console.WriteLine("--------------------------------------------------\n");
                Thread.Sleep(10000);
            }
        }

        static FileInfo getSettingFile(string[] args)
        {
            // 如果运行参数中给出且文件存在 则直接返回
            if (args.Length > 0 && File.Exists(args[0]))
            {
                return new FileInfo(args[0]);
            }
            // 其他情况，读取程序所在目录下所有的.json/.txt文件 提供给用户选择
            List<FileInfo> allFolderFiles = new List<FileInfo>(new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory).GetFiles());
            List<FileInfo> toChooseFiles = new List<FileInfo>();
            foreach (FileInfo file in allFolderFiles)
            {
                if (file.Name.ToLower().EndsWith(".json") || file.Name.ToLower().EndsWith(".txt"))
                {
                    // 对于toChooseFiles中的文件逐个反序列化尝试看是否是符合标准的配置文件
                    try
                    {
                        getSetting(file.FullName);
                        toChooseFiles.Add(file);
                    }
                    catch (Exception)
                    {
                        continue;
                    }
                }
            }
            // 如果没有符合条件的配置文件，则直接退出
            if (toChooseFiles.Count == 0)
            {
                Console.WriteLine($"程序目录下没有找到符合配置文件格式的文件！");
                throw new Exception("程序目录下没有找到符合配置文件格式的文件");
            }

            // 输出提示，让用户选择
            Console.WriteLine($"未能够自动找到合适的配置文件，找到{toChooseFiles.Count}个目录下文件：");
            for (int index = 0; index < toChooseFiles.Count + 1; index++)
            {
                if (index == toChooseFiles.Count)
                {
                    Console.WriteLine($"【{index}】 退出");
                }
                else
                {
                    Console.WriteLine($"【{index}】 {toChooseFiles[index].Name}");
                }
            }
            Console.Write("请选择要执行的操作：");
            string chooseInput = Console.ReadLine()!;
            int chooseNum = int.Parse(chooseInput);
            if (chooseNum >= 0 && chooseNum <= toChooseFiles.Count)
            {
                if (chooseNum == toChooseFiles.Count)
                {
                    throw new Exception("退出程序！");
                }
                Console.WriteLine($"选择了第【{chooseNum}】项: {toChooseFiles[chooseNum].Name}");
                return toChooseFiles[chooseNum];
            }
            else
            {
                throw new Exception("未选择有效的项目，退出程序！");
            }
        }

        static List<SettingItem> getSetting(string path)
        {
            return JsonSerializer.Deserialize<List<SettingItem>>(File.ReadAllText(path))!;
        }

        static DirectoryInfo prepareWorkFolder()
        {
            String tempFolderPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "workingFolder");
            DirectoryInfo programTempFolderDirectoryInfo = new DirectoryInfo(tempFolderPath);
            if (!programTempFolderDirectoryInfo.Exists)
            {
                programTempFolderDirectoryInfo.Create();
            }
            return programTempFolderDirectoryInfo;
        }

        static string getTaskKey(SettingItem settingItem, List<SettingItem> settingJson, FileInfo programSettingFileinfo)
        {
            StringBuilder stringBuilder = new StringBuilder();
            stringBuilder
                .Append(Environment.ProcessPath)
                .Append(programSettingFileinfo.FullName)
                .Append(JsonSerializer.Serialize(settingJson))
                .Append(settingItem.taskName)
                .Append(settingItem.workFolder)
                .Append(settingItem.programName)
                .Append(settingItem.runParameter);

            byte[] hashResult = SHA256.HashData(Encoding.UTF8.GetBytes(stringBuilder.ToString()));
            return convertHashCodeBytes(hashResult);
        }

        static string convertHashCodeBytes(byte[] hashvalue)
        {
            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < hashvalue.Length; i++)
                builder.Append(hashvalue[i].ToString("x2"));
            return builder.ToString();
        }

        static string? getPidContent(FileInfo fileInfo)
        {
            FileInfo newFileInfo = new FileInfo(fileInfo.FullName);
            if (newFileInfo.Exists)
            {
                string allContent = File.ReadAllText(newFileInfo.FullName);
                return allContent.Trim(' ', '\r', '\n');
            }
            else
            {
                return null;
            }
        }

        static void refreshAllTaskStatus(List<SettingItem> settingJson,
            Dictionary<SettingItem, FileInfo> taskItemFileDict,
            Dictionary<SettingItem, bool> taskItemRunningStatus,
            Dictionary<SettingItem, int> taskItemPid
            )
        {
            for (int i = 0; i < settingJson.Count; i++)
            {
                SettingItem item = settingJson[i];
                string? pid = getPidContent(taskItemFileDict[item]);
                if (null == pid)
                {
                    taskItemRunningStatus[item] = false;
                    taskItemPid.Remove(item);
                }
                else
                {
                    bool isRun = false;
                    Process process = null;
                    try
                    {
                        process = Process.GetProcessById(int.Parse(pid));
                        FileInfo fromSetting = new FileInfo(item.programName);
                        FileInfo fromEnviroment = new FileInfo(process.MainModule.FileName);
                        isRun = !process.HasExited && fromSetting.FullName.Equals(fromEnviroment.FullName);
                    }
                    catch (Exception)
                    {
                        isRun = false;
                    }
                    if (isRun)
                    {
                        taskItemRunningStatus[item] = true;
                        taskItemPid[item] = int.Parse(pid);
                    }
                    else
                    {
                        taskItemRunningStatus[item] = false;
                        taskItemPid.Remove(item);
                    }
                }
            }
        }

        static Process runCMD_Linux(SettingItem settingItem)
        {
            Process process = new Process();
            process.StartInfo.FileName = "bash";
            process.StartInfo.Arguments = $"-c \"{settingItem.programName} {settingItem.runParameter}\"";
            process.StartInfo.WorkingDirectory = settingItem.workFolder;
            process.StartInfo.CreateNoWindow = true;
            process.Start();

            return process;
        }

        static Process runCMD_Windows(SettingItem settingItem)
        {
            Process process = new Process();

            process.StartInfo.FileName = settingItem.programName;
            process.StartInfo.Arguments = settingItem.runParameter;
            process.StartInfo.WorkingDirectory = settingItem.workFolder;
            process.StartInfo.CreateNoWindow = true;
            process.StartInfo.UseShellExecute = true;

            process.Start();
            return process;
        }
    }
}
