using ReactiveUI;
using Splat;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Windows;
using System.Xml.Linq;
using WolvenKit.App.Models;
using WolvenKit.Common;
using WolvenKit.Common.Interfaces;
using WolvenKit.Common.Services;
using WolvenKit.Core.Compression;
using WolvenKit.Core.Interfaces;
using WolvenKit.Core.Services;
using WolvenKit.Functionality.Services;
using WolvenKit.Helpers;
using WolvenKit.Models;
using WolvenKit.ProjectManagement.Project;
using WolvenKit.RED4.CR2W;
using WolvenKit.RED4.Types;
using static WolvenKit.RED4.Types.Enums;

namespace WolvenKit.Functionality.Controllers
{
    public class RED4Controller : ReactiveObject, IGameController
    {
        #region fields

        public const string GameVersion = "1.6.0";

        private readonly ILoggerService _loggerService;
        private readonly INotificationService _notificationService;
        private readonly IProjectManager _projectManager;
        private readonly ISettingsManager _settingsManager;
        private readonly IHashService _hashService;
        private readonly IModTools _modTools;
        private readonly IArchiveManager _archiveManager;
        private readonly IProgressService<double> _progressService;
        private readonly IPluginService _pluginService;

        private bool _initialized = false;

        #endregion

        public RED4Controller(
            ILoggerService loggerService,
            INotificationService notificationService,
            IProjectManager projectManager,
            ISettingsManager settingsManager,
            IHashService hashService,
            IModTools modTools,
            IArchiveManager gameArchiveManager,
            IProgressService<double> progressService,
            IPluginService pluginService
            )
        {
            _notificationService = notificationService;
            _loggerService = loggerService;
            _projectManager = projectManager;
            _settingsManager = settingsManager;
            _hashService = hashService;
            _modTools = modTools;
            _archiveManager = gameArchiveManager;
            _progressService = progressService;
            _pluginService = pluginService;
        }

        public Task HandleStartup()
        {
            if (!_initialized)
            {
                _initialized = true;

                // load archives
                List<Func<IArchiveManager>> todo = new()
                {
                    LoadArchiveManager,
                };
                Parallel.ForEach(todo, _ => Task.Run(_));

                // requires oodle
                InitializeBk();

            }

            return Task.CompletedTask;
        }

        // TODO: Move this somewhere else
        private void LoadCustomHashes()
        {
            Red4ParserService parser = Locator.Current.GetService<Red4ParserService>();

            CName physMatLibPath = "base\\physics\\physicsmaterials.physmatlib";
            CName presetPath = "engine\\physics\\collision_presets.json";

            DynamicData.Kernel.Optional<IGameFile> physMatLib = _archiveManager.Lookup(physMatLibPath);
            if (physMatLib.HasValue)
            {
                using MemoryStream ms = new();
                physMatLib.Value.Extract(ms);
                ms.Position = 0;

                if (parser.TryReadRed4File(ms, out RED4.Archive.CR2W.CR2WFile file))
                {
                    physicsMaterialLibraryResource root = (physicsMaterialLibraryResource)file.RootChunk;

                    foreach (CName physMat in root.Unk1)
                    {
                        _hashService.AddCustom(physMat);
                    }
                }
            }

            DynamicData.Kernel.Optional<IGameFile> preset = _archiveManager.Lookup(presetPath);
            if (preset.HasValue)
            {
                using MemoryStream ms = new();
                preset.Value.Extract(ms);
                ms.Position = 0;

                if (parser.TryReadRed4File(ms, out RED4.Archive.CR2W.CR2WFile file))
                {
                    JsonResource root = (JsonResource)file.RootChunk;
                    physicsCollisionPresetsResource res = (physicsCollisionPresetsResource)root.Root.Chunk;

                    foreach (physicsCollisionPresetDefinition presetEntry in res.Presets)
                    {
                        _hashService.AddCustom(presetEntry.Name);
                    }
                }
            }
        }

        private void InitializeBk()
        {
            string[] binkhelpers = { @"Resources\Media\t1.kark", @"Resources\Media\t2.kark", @"Resources\Media\t3.kark", @"Resources\Media\t4.kark", @"Resources\Media\t5.kark" };

            if (string.IsNullOrEmpty(_settingsManager.GetRED4GameRootDir()))
            {
                Trace.WriteLine("That worked to cancel Loading oodle! :D");
                return;
            }

            foreach (string path in binkhelpers)
            {
                switch (path)
                {
                    case @"Resources\Media\t1.kark":
                        if (!File.Exists(Path.Combine(ISettingsManager.GetWorkDir(), "test.exe")))
                        {
                            _ = Oodle.OodleTask(path, Path.Combine(ISettingsManager.GetWorkDir(), "test.exe"), true,
                                false);
                        }

                        break;

                    case @"Resources\Media\t2.kark":
                        if (!File.Exists(Path.Combine(ISettingsManager.GetWorkDir(), "testconv.exe")))
                        {
                            _ = Oodle.OodleTask(path, Path.Combine(ISettingsManager.GetWorkDir(), "testconv.exe"), true,
                                false);
                        }

                        break;

                    case @"Resources\Media\t3.kark":
                        if (!File.Exists(Path.Combine(ISettingsManager.GetWorkDir(), "testc.exe")))
                        {
                            _ = Oodle.OodleTask(path, Path.Combine(ISettingsManager.GetWorkDir(), "testc.exe"), true,
                                false);
                        }

                        break;

                    case @"Resources\Media\t4.kark":
                        if (!File.Exists(Path.Combine(ISettingsManager.GetWorkDir(), "radutil.dll")))
                        {
                            _ = Oodle.OodleTask(path, Path.Combine(ISettingsManager.GetWorkDir(), "radutil.dll"), true,
                                false);
                        }

                        break;

                    case @"Resources\Media\t5.kark":
                        if (!File.Exists(Path.Combine(ISettingsManager.GetWorkDir(), "bink2make.dll")))
                        {
                            _ = Oodle.OodleTask(path, Path.Combine(ISettingsManager.GetWorkDir(), "bink2make.dll"), true,
                                false);
                        }

                        break;
                }
            }
        }

        private IArchiveManager LoadArchiveManager()
        {
            if (_archiveManager != null && _archiveManager.IsManagerLoaded)
            {
                return _archiveManager;
            }

            _loggerService.Info("Loading Archive Manager ... ");
            try
            {
                _archiveManager.LoadGameArchives(new FileInfo(_settingsManager.CP77ExecutablePath));
            }
            catch (Exception e)
            {
                _loggerService.Error(e);
                throw;
            }
            finally
            {
                _loggerService.Success("Finished loading Archive Manager.");
            }

            LoadCustomHashes();

#pragma warning disable 162
            return _archiveManager;
#pragma warning restore 162
        }

        #region Packing

        private bool PackProjectNoBackup()
        {

            if (_projectManager.ActiveProject is not Cp77Project cp77Proj)
            {
                _loggerService.Error("Can't pack project (no project/not cyberpunk project)!");
                return false;
            }

            // cleanup
            try
            {
                string[] archives = Directory.GetFiles(cp77Proj.PackedArchiveDirectory, "*.archive");
                foreach (string archive in archives)
                {
                    File.Delete(archive);
                }
            }
            catch (Exception e)
            {
                _loggerService.Error(e);
            }

            // pack mod
            string[] modfiles = Directory.GetFiles(cp77Proj.ModDirectory, "*", SearchOption.AllDirectories);
            if (modfiles.Any())
            {
                _modTools.Pack(
                    new DirectoryInfo(cp77Proj.ModDirectory),
                    new DirectoryInfo(cp77Proj.PackedArchiveDirectory),
                    cp77Proj.Name);
                _loggerService.Info("Packing archives complete!");
            }
            _loggerService.Success($"{cp77Proj.Name} packed into {cp77Proj.PackedArchiveDirectory}");

            // compile tweak files
            PackTweakXlFiles(cp77Proj);

            return true;
        }

        private bool PackProjectHot()
        {

            if (_projectManager.ActiveProject is not Cp77Project cp77Proj)
            {
                _loggerService.Error("Can't pack project (no project/not cyberpunk project)!");
                return false;
            }

            string hotdirectory = Path.Combine(_settingsManager.GetRED4GameRootDir(), "archive", "pc", "hot");

            // create hot directory
            if (!Directory.Exists(hotdirectory))
            {
                Directory.CreateDirectory(hotdirectory);
                _loggerService.Info($"Created hot directory at {hotdirectory}");
            }

            // pack mod
            string[] modfiles = Directory.GetFiles(cp77Proj.ModDirectory, "*", SearchOption.AllDirectories);
            if (modfiles.Any())
            {
                _modTools.Pack(
                    new DirectoryInfo(cp77Proj.ModDirectory),
                    new DirectoryInfo(hotdirectory),
                    cp77Proj.Name);
                _loggerService.Info("Hot archive installation complete!");
            }
            _loggerService.Success($"{cp77Proj.Name} packed into {hotdirectory}");

            return true;
        }


        /// <summary>
        /// Pack mod with options
        /// </summary>
        /// <returns></returns>
        public async Task<bool> LaunchProject(LaunchProfile options)
        {
            _progressService.IsIndeterminate = true;

            // check
            if (_projectManager.ActiveProject is not Cp77Project cp77Proj)
            {
                _progressService.IsIndeterminate = false;
                _loggerService.Error("Cannot pack project (no project/not cyberpunk project)!");
                return false;
            }

            // cleanup
            if (!Cleanup(cp77Proj))
            {
                _progressService.IsIndeterminate = false;
                _loggerService.Error("Cleanup failed, aborting.");
                return false;
            }
            _loggerService.Success($"{cp77Proj.Name} files cleaned up.");

            // copy files to packed dir
            // pack archives
            if (!PackArchives(cp77Proj))
            {
                _progressService.IsIndeterminate = false;
                _loggerService.Error("Packing archives failed, aborting.");
                return false;
            }
            _loggerService.Success($"{cp77Proj.Name} archives packed into {cp77Proj.PackedArchiveDirectory}");

            // pack tweakXL files
            if (!PackTweakXlFiles(cp77Proj))
            {
                _progressService.IsIndeterminate = false;
                _loggerService.Error("Packing tweakXL files failed, aborting.");
                return false;
            }
            _loggerService.Success($"{cp77Proj.Name} tweakXL files packed into {cp77Proj.PackedTweakDirectory}");

            // pack redmod files
            if (!PackRedmodFiles(cp77Proj))
            {
                _progressService.IsIndeterminate = false;
                _loggerService.Error("Packing redmod files failed, aborting.");
                return false;
            }
            _loggerService.Success($"{cp77Proj.Name} redmod files packed into {cp77Proj.PackedModDirectory}");

            // backup
            if (options.CreateBackup)
            {
                if (!BackupMod(cp77Proj))
                {
                    _progressService.IsIndeterminate = false;
                    _loggerService.Error("Creating backup failed, aborting.");
                    return false;
                }
            }

            // install files
            if (options.Install)
            {
                if (!InstallMod(cp77Proj))
                {
                    _progressService.IsIndeterminate = false;
                    _loggerService.Error("Installing mod failed, aborting.");
                    return false;
                }
                _loggerService.Success($"{cp77Proj.Name} installed!");
            }

            // deploy redmod
            if (options.DeployWithRedmod)
            {
                if (!await DeployRedmod())
                {
                    _progressService.IsIndeterminate = false;
                    _loggerService.Error("Redmod deploy failed, aborting.");
                    return false;
                }
                _loggerService.Success($"{cp77Proj.Name} Redmod deployed!");
            }

            // success
            _notificationService.Success($"{cp77Proj.Name} installed!");
            _progressService.IsIndeterminate = false;

            // launch game
            if (options.LaunchGame)
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = _settingsManager.GetRED4GameLaunchCommand(),
                    Arguments = options.GameArguments ?? "",
                    ErrorDialog = true,
                    UseShellExecute = true,
                });
            }

            return true;
        }

        private bool Cleanup(Cp77Project cp77Proj)
        {
            try
            {
                string[] archives = Directory.GetFiles(cp77Proj.PackedArchiveDirectory, "*.archive");
                foreach (string archive in archives)
                {
                    File.Delete(archive);
                }
            }
            catch (Exception e)
            {
                _loggerService.Error(e);
                return false;
            }

            return true;
        }

        private bool PackArchives(Cp77Project cp77Proj)
        {
            string[] modfiles = Directory.GetFiles(cp77Proj.ModDirectory, "*", SearchOption.AllDirectories);
            if (modfiles.Any())
            {
                _modTools.Pack(
                    new DirectoryInfo(cp77Proj.ModDirectory),
                    new DirectoryInfo(cp77Proj.PackedArchiveDirectory),
                    cp77Proj.Name);
                _loggerService.Info("Packing archives complete!");
            }

            return true;
        }
        private bool PackTweakXlFiles(Cp77Project cp77Proj)
        {
            try
            {
                Directory.Delete(cp77Proj.PackedTweakDirectory, true);
            }
            catch (Exception e)
            {
                _loggerService.Error(e);
                return false;
            }

            string[] tweakFiles = Directory.GetFiles(cp77Proj.TweakDirectory, "*.yaml", SearchOption.AllDirectories);
            foreach (string f in tweakFiles)
            {
                string folder = Path.GetDirectoryName(Path.GetRelativePath(cp77Proj.TweakDirectory, f));
                string outDirectory = Path.Combine(cp77Proj.PackedTweakDirectory, folder);
                if (!Directory.Exists(outDirectory))
                {
                    Directory.CreateDirectory(outDirectory);
                }
                string filename = Path.GetFileName(f);
                string outPath = Path.Combine(outDirectory, filename);
                File.Copy(f, outPath, true);
            }
            return true;
        }
        private bool PackRedmodFiles(Cp77Project cp77Proj)
        {
            if (cp77Proj.IsRedMod)
            {
                // sounds
                PackSoundFiles();

                // tweaks
                // TODO

                // scripts
                // TODO

                // write info.json file if it not exists
                string modInfoJsonPath = Path.Combine(cp77Proj.PackedModDirectory, "info.json");
                if (!File.Exists(modInfoJsonPath))
                {
                    JsonSerializerOptions jsonoptions = new()
                    {
                        WriteIndented = true,
                        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                    };
                    string jsonString = JsonSerializer.Serialize(cp77Proj.GetInfo(), jsonoptions);
                    File.WriteAllText(modInfoJsonPath, jsonString);
                }
            }
            else
            {
                if (Directory.EnumerateFileSystemEntries(cp77Proj.SoundDirectory).Any())
                {
                    _loggerService.Warning("This project contains custom sound files but is packed as legacy mod!");
                    return false;
                }
            }
            return true;
        }
        private void PackSoundFiles()
        {
            string path = Path.Combine(_projectManager.ActiveProject.PackedModDirectory, "info.json");
            if (!File.Exists(path))
            {
                return;
            }

            // read info
            Cp77Project modProj = _projectManager.ActiveProject as Cp77Project;
            List<string> files = new();
            try
            {
                // clean packed sounds dir
                foreach (string f in Directory.GetFiles(modProj.PackedSoundsDirectory))
                {
                    File.Delete(f);
                }

                JsonSerializerOptions options = new()
                {
                    WriteIndented = true,
                    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    IgnoreReadOnlyProperties = true,
                };
                ModInfo info = JsonSerializer.Deserialize<ModInfo>(File.ReadAllText(path), options);
                foreach (Modkit.RED4.Sounds.CustomSoundsModel e in info.CustomSounds)
                {
                    if (!string.IsNullOrEmpty(e.File))
                    {
                        files.Add(e.File);

                        string rawFile = Path.Combine(modProj.SoundDirectory, e.File);
                        string packedFile = Path.Combine(modProj.PackedSoundsDirectory, e.File);
                        if (File.Exists(rawFile))
                        {
                            File.Copy(rawFile, packedFile, true);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                _loggerService.Error(e);
            }
        }

        private bool BackupMod(Cp77Project cp77Proj)
        {
            string zipPathRoot = new DirectoryInfo(cp77Proj.PackedRootDirectory).Parent.FullName;
            string zipPath = Path.Combine(zipPathRoot, $"{cp77Proj.Name}.zip");
            try
            {
                if (File.Exists(zipPath))
                {
                    File.Delete(zipPath);
                }
                ZipFile.CreateFromDirectory(cp77Proj.PackedRootDirectory, zipPath);
            }
            catch (Exception e)
            {
                _loggerService.Error(e);
                return false;
            }

            _loggerService.Success($"{cp77Proj.Name} zip available at {zipPath}");
            return true;
        }

        private bool InstallMod(Cp77Project activeMod)
        {
            string logPath = Path.Combine(activeMod.ProjectDirectory, "install_log.xml");

            try
            {
                //Check if we have installed this mod before. If so do a little cleanup.
                if (File.Exists(logPath))
                {
                    XDocument log = XDocument.Load(logPath);
                    List<XElement> dirs = log.Root.Element("Files")?.Descendants("Directory").ToList();
                    if (dirs != null)
                    {
                        //Loop throught dirs and delete the old files in them.
                        foreach (XElement d in dirs)
                        {
                            foreach (XElement f in d.Elements("file"))
                            {
                                if (File.Exists(f.Value))
                                {
                                    File.Delete(f.Value);
                                    Debug.WriteLine("File delete: " + f.Value);
                                }
                            }
                        }
                        //Delete the empty directories.
                        foreach (XElement d in dirs)
                        {
                            if (d.Attribute("Path") != null
                                && Directory.Exists(d.Attribute("Path").Value)
                                && !Directory.GetFiles(d.Attribute("Path").Value, "*", SearchOption.AllDirectories).Any())
                            {
                                Directory.Delete(d.Attribute("Path").Value, true);
                                Debug.WriteLine("Directory delete: " + d.Attribute("Path").Value);
                            }
                        }
                    }
                    //Delete the old install log. We will make a new one so this is not needed anymore.
                    File.Delete(logPath);
                }

                XDocument installlog = new(
                    new XElement("InstalLog",
                        new XAttribute("Project", activeMod.Name),
                        new XAttribute("Build_date", DateTime.Now.ToString())
                        ));
                XElement fileroot = new("Files");

                //Copy and log the files.
                string packedmoddir = activeMod.PackedRootDirectory;
                if (!Directory.Exists(packedmoddir))
                {
                    _loggerService.Error("Failed to install the mod! The packed directory doesn't exist!");
                    return false;
                }

                fileroot.Add(Commonfunctions.DirectoryCopy(packedmoddir, _settingsManager.GetRED4GameRootDir(), true));

                //var packeddlcdir = Path.Combine(ActiveMod.ProjectDirectory, "packed", "DLC");
                //if (Directory.Exists(packeddlcdir))
                //    fileroot.Add(Commonfunctions.DirectoryCopy(packeddlcdir, MainController.Get().Configuration.CP77GameDlcDir, true));

                installlog.Root.Add(fileroot);
                installlog.Save(logPath);


            }
            catch (Exception ex)
            {
                //If we screwed up something. Log it.
                _loggerService.Error(ex);
                return false;
            }

            return true;
        }

        private Task<bool> DeployRedmod()
        {
            if (!_pluginService.IsInstalled(EPlugin.redmod))
            {
                return Task.FromResult(false);
            }

            // compile with redmod
            string redmodPath = Path.Combine(_settingsManager.GetRED4GameRootDir(), "tools", "redmod", "bin", "redMod.exe");
            if (File.Exists(redmodPath))
            {
                string rttiSchemaPath = Path.Combine(_settingsManager.GetRED4GameRootDir(), "tools", "redmod", "metadata.json");
                string args = $"deploy -root=\"{_settingsManager.GetRED4GameRootDir()}\" -rttiSchemaPath=\"{rttiSchemaPath}\"";

                _loggerService.Info($"WorkDir: {redmodPath}");
                _loggerService.Info($"Running commandlet: {args}");
                return ProcessUtil.RunProcessAsync(redmodPath, args);
            }

            return Task.FromResult(true);
        }

        #endregion

        public void AddToMod(ulong hash)
        {
            DynamicData.Kernel.Optional<IGameFile> file = _archiveManager.Lookup(hash);
            if (file.HasValue)
            {
                AddToMod(file.Value);
            }
        }

        public void AddToMod(IGameFile file)
        {
            EditorProject project = _projectManager.ActiveProject;
            switch (project.GameType)
            {
                case GameType.Witcher3:
                    {
                        //if (project is Tw3Project witcherProject)
                        //{
                        //    var diskPathInfo = new FileInfo(Path.Combine(witcherProject.ModCookedDirectory, file.Name));
                        //    if (diskPathInfo.Directory == null)
                        //    {
                        //        break;
                        //    }

                        //    Directory.CreateDirectory(diskPathInfo.Directory.FullName);
                        //    using var fs = new FileStream(diskPathInfo.FullName, FileMode.Create);
                        //    file.Extract(fs);
                        //}
                        break;
                    }
                case GameType.Cyberpunk2077:
                    {
                        if (project is Cp77Project cyberpunkProject)
                        {
                            string fileName = file.Name;
                            if (file.Name == file.Key.ToString() && file.GuessedExtension != null)
                            {
                                fileName += file.GuessedExtension;
                            }

                            FileInfo diskPathInfo = new(Path.Combine(cyberpunkProject.ModDirectory, fileName));
                            if (diskPathInfo.Directory == null)
                            {
                                break;
                            }

                            if (File.Exists(diskPathInfo.FullName))
                            {
                                if (MessageBox.Show($"The file {file.Name} already exists in project - overwrite it with game file?", $"Confirm overwrite: {file.Name}", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                                {
                                    using FileStream fs = new(diskPathInfo.FullName, FileMode.Create);
                                    file.Extract(fs);
                                    _loggerService.Success($"Overwrote existing file with game file: {file.Name}");
                                }
                                else
                                {
                                    _loggerService.Info($"Declined to overwrite existing file: {file.Name}");
                                }
                            }
                            else
                            {
                                Directory.CreateDirectory(diskPathInfo.Directory.FullName);
                                using FileStream fs = new(diskPathInfo.FullName, FileMode.Create);
                                file.Extract(fs);
                                _loggerService.Success($"Added game file to project: {file.Name}");
                            }
                        }

                        break;
                    }
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }
}
