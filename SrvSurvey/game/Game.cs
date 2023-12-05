﻿using SrvSurvey.canonn;
using SrvSurvey.net;
using SrvSurvey.net.EDSM;
using SrvSurvey.units;
using System.Diagnostics;
using System.Reflection;

namespace SrvSurvey.game
{
    /// <summary>
    /// Represents live state of the game Elite Dangerous
    /// </summary>
    class Game : IDisposable
    {
        static Game()
        {
            Game.logs = new List<string>();
            Game.logPath = prepLogFile();
            Game.log($"SrvSurvey version: {Game.releaseVersion}");
            Game.log($"New log file: {Game.logPath}");
            Game.removeExcessLogFiles();

            settings = Settings.Load();
            codexRef = new CodexRef();
            canonn = new Canonn();
            spansh = new Spansh();
            edsm = new EDSM();
        }

        #region logging

        private static string prepLogFile()
        {
            // prepare filepath for log file
            Directory.CreateDirectory(Game.logFolder);
            var datepart = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var filepath = Path.Combine(Game.logFolder, $"srvs-{datepart}.txt")!;
            File.WriteAllLines(filepath, Game.logs);

            return filepath;
        }

        public static void log(object? msg)
        {
            var txt = DateTime.Now.ToString("HH:mm:ss") + ": " + msg?.ToString();

            Debug.WriteLine(txt);

            Game.logs.Add(txt);

            ViewLogs.append(txt);

            File.AppendAllText(Game.logPath, txt + "\r\n");
        }

        private static void removeExcessLogFiles()
        {
            var logFiles = new DirectoryInfo(Game.logFolder)
                .EnumerateFiles("*.txt", SearchOption.TopDirectoryOnly)
                .OrderByDescending(_ => _.LastWriteTimeUtc)
                .ToList();

            for (var idx = 5; idx < logFiles.Count; idx++)
            {
                var filename = logFiles[idx].FullName;
                Game.log($"Removing old log file: {filename}");
                File.Delete(filename);
            }
        }

        public static readonly List<string> logs;
        private static readonly string logPath;
        public static string logFolder = Path.Combine(Application.UserAppDataPath, "logs", "");
        public static string releaseVersion = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version!;

        #endregion

        public static Game? activeGame { get; private set; }
        public static Settings settings { get; private set; }
        public static CodexRef codexRef { get; private set; }
        public static Canonn canonn { get; private set; }
        public static Spansh spansh { get; private set; }
        public static EDSM edsm { get; private set; }

        public bool initialized { get; private set; }

        /// <summary>
        /// The Commander actively playing the game
        /// </summary>
        public string? Commander { get; private set; }
        public string? fid { get; private set; }
        public bool isOdyssey { get; private set; }
        public string musicTrack { get; private set; }
        public SuitType currentSuitType { get; private set; }

        public JournalWatcher? journals;
        public Status status { get; private set; }
        public NavRouteFile navRoute { get; private set; }

        public SystemStatus systemStatus;

        public SystemData? systemData;
        public SystemBody? systemBody;

        /// <summary>
        /// Distinct settings for the current commander
        /// </summary>
        public CommanderSettings cmdr;

        public SystemPoi? canonnPoi = null;

        public Game(string? cmdr)
        {
            log($"Game .ctor");

            // track this instance as the active one
            Game.activeGame = this;

            if (!this.isRunning) return;

            // track status file changes and force an immediate read
            this.status = new Status(true);
            this.navRoute = NavRouteFile.load(true);

            // initialize from a journal file
            var filepath = JournalFile.getCommanderJournalBefore(cmdr, true, DateTime.MaxValue);
            if (filepath == null)
            {
                Game.log($"No journal files found for: {cmdr}");
                this.isShutdown = true;
                return;
            }

            this.journals = new JournalWatcher(filepath);
            this.initializeFromJournal();
            if (this.isShutdown) return;

            log($"Cmdr loaded: {this.Commander != null}");

            // now listen for changes
            this.journals.onJournalEntry += Journals_onJournalEntry;
            this.status.StatusChanged += Status_StatusChanged;

            Status_StatusChanged(false);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (this.nearBody != null)
                {
                    this.nearBody.Dispose();
                    this.nearBody = null;

                }

                if (this.journals != null)
                {
                    this.journals.onJournalEntry -= Journals_onJournalEntry;
                    this.journals.Dispose();
                    this.journals = null;
                }

                if (this.status != null)
                {
                    this.status.StatusChanged -= Status_StatusChanged;
                    this.status.Dispose();
                    this.status = null!;
                }

                if (this.navRoute != null)
                {
                    this.navRoute.Dispose();
                    this.navRoute = null!;
                }
            }
        }

        #region modes

        public void fireUpdate(bool force = false)
        {
            this.fireUpdate(this.mode, force);
        }

        public void fireUpdate(GameMode newMode, bool force)
        {
            if (Game.update != null) Game.update(newMode, force);
        }

        public event GameNearingBody? nearingBody;
        public event GameDepartingBody? departingBody;
        public static event GameModeChanged? update;

        public bool atMainMenu = false;
        public bool atCarrierMgmt = false;
        public bool fsdJumping = false;
        public bool isShutdown = false;
        public LandableBody? nearBody;
        /// <summary>
        /// Tracks if we status had Lat/Long last time we knew
        /// </summary>
        private bool statusHasLatLong = false;
        /// <summary>
        /// Tracks BodyName from status - attempting to detect when multiple running games clobber the same file in error
        /// </summary>
        private string? statusBodyName;

        private void checkModeChange()
        {
            // check various things we actively need to know have changed
            if (this._mode != this.mode)
            {
                log($"Mode change {this._mode} => {this.mode}");
                this._mode = this.mode;

                // fire event!
                fireUpdate(this._mode, false);
            }
        }

        private void Status_StatusChanged(bool blink)
        {
            //log("status changed");
            if (this.statusBodyName != null && status!.BodyName != null && status!.BodyName != this.statusBodyName)
            {
                // exit early if the body suddenly switches to something different without being NULL first
                Game.log($"Multiple running games? this.statusBodyName: ${this.statusBodyName} vs status.BodyName: {status.BodyName}");
                // TODO: some planet/moons are close enough together to trigger this - maybe compare the first word from each?
                return;
            }
            this.statusBodyName = status!.BodyName;

            // track if status HasLatLong, toggling behaviours when it changes
            var newHasLatLong = status.hasLatLong;
            if (this.statusHasLatLong != newHasLatLong)
            {
                Game.log($"Changed hasLatLong: {newHasLatLong}, has nearBody: {this.nearBody != null}, nearBody matches: {status.BodyName == this.nearBody?.bodyName}");
                this.statusHasLatLong = newHasLatLong;

                if (!newHasLatLong)
                {
                    // we are departing
                    if (this.departingBody != null && this.nearBody != null)
                    {
                        Game.log($"EVENT:departingBody, from {this.nearBody.bodyName}");

                        // change system locations to be just system (not the body)
                        cmdr.currentBody = null;
                        cmdr.currentBodyId = -1;
                        cmdr.currentBodyRadius = -1;
                        cmdr.Save();

                        this.systemBody = null;

                        // fire event
                        var leftBody = this.nearBody;
                        this.nearBody.Dispose();
                        this.nearBody = null;
                        this.departingBody(leftBody);
                    }
                }
                else
                {
                    if (this.nearBody == null && status.BodyName != null)
                    {
                        // we are approaching - create and fire event
                        this.createNearBody(status.BodyName);
                    }
                }
            }

            if (!status.hasLatLong && this.systemBody != null)
            {
                log($"status change clearing systemBody from: '{this.systemBody.name}' ({this.systemBody.id})");
                this.systemBody = null;
            }

            this.checkModeChange();
        }

        private GameMode _mode;
        public GameMode mode
        {
            get
            {
                if (!this.isRunning || this.isShutdown)
                    return GameMode.Offline;

                if (this.atMainMenu || this.Commander == null)
                    return GameMode.MainMenu;

                if (this.atCarrierMgmt)
                    return GameMode.CarrierMgmt;

                // use GuiFocus if it is interesting
                if (this.status?.GuiFocus != GuiFocus.NoFocus)
                    return (GameMode)(int)this.status!.GuiFocus;

                if (this.fsdJumping)
                    return GameMode.FSDJumping;

                // otherwise use the type of vehicle we are in
                var activeVehicle = this.vehicle;
                if (activeVehicle == ActiveVehicle.Fighter)
                    return GameMode.InFighter;
                if (activeVehicle == ActiveVehicle.SRV)
                    return GameMode.InSrv;
                if (activeVehicle == ActiveVehicle.Taxi)
                    return GameMode.InTaxi;
                if ((this.status.Flags2 & StatusFlags2.OnFootExterior) > 0)
                    return GameMode.OnFoot;

                // otherwise we are in the main ship, travelling ...
                if ((this.status.Flags & StatusFlags.Supercruise) > 0)
                    return GameMode.SuperCruising;
                if ((this.status.Flags2 & StatusFlags2.GlideMode) > 0)
                    return GameMode.GlideMode;

                if ((this.status.Flags & StatusFlags.Landed) > 0)
                    return GameMode.Landed;

                // or we maybe ...
                if ((this.status.Flags & StatusFlags.Docked) > 0)
                    return GameMode.Docked;
                if ((this.status.Flags2 & StatusFlags2.OnFootInStation) > 0)
                    return GameMode.Docked;
                if ((this.status.Flags2 & StatusFlags2.OnFootSocialSpace) > 0)
                    return GameMode.Social;

                // otherwise we must be ...
                if (activeVehicle == ActiveVehicle.MainShip)
                    return GameMode.Flying;

                Game.log($"Unknown game mode? status.Flags: {status.Flags}, status.Flags2: {status.Flags2}");
                return GameMode.Unknown;
            }
        }

        public bool isMode(params GameMode[] modes)
        {
            return modes.Contains(this.mode);
        }

        public ActiveVehicle vehicle
        {
            get
            {
                if (this.status == null)
                    return ActiveVehicle.Unknown;
                else if ((this.status.Flags & StatusFlags.InMainShip) > 0)
                    return ActiveVehicle.MainShip;
                else if ((this.status.Flags & StatusFlags.InFighter) > 0)
                    return ActiveVehicle.Fighter;
                else if ((this.status.Flags & StatusFlags.InSRV) > 0)
                    return ActiveVehicle.SRV;
                else if ((this.status.Flags2 & StatusFlags2.OnFoot) > 0)
                    return ActiveVehicle.Foot;
                else if ((this.status.Flags2 & StatusFlags2.InTaxi) > 0)
                    return ActiveVehicle.Taxi;
                else
                    //return ActiveVehicle.Docked;
                    return ActiveVehicle.Unknown;

                // TODO: do we care about telepresence?
            }
        }

        public bool isLanded
        {
            get => this.isMode(GameMode.InSrv, GameMode.OnFoot, GameMode.Landed);
        }

        public bool hidePlottersFromCombatSuits
        {
            get => Game.settings.hidePlottersFromCombatSuits
                && (this.status.Flags2 & StatusFlags2.OnFoot) > 0
                && (this.currentSuitType == SuitType.dominator || this.currentSuitType == SuitType.maverick);
        }

        public bool showBodyPlotters
        {
            get => !this.isShutdown
                && !this.atMainMenu
                && this.isMode(GameMode.SuperCruising, GameMode.Flying, GameMode.Landed, GameMode.InSrv, GameMode.OnFoot, GameMode.GlideMode, GameMode.InFighter, GameMode.CommsPanel)
                && !this.hidePlottersFromCombatSuits;
        }

        public bool showGuardianPlotters
        {
            get => Game.settings.enableGuardianSites
                && this.nearBody?.siteData != null
                && this.nearBody?.siteData.location != null // ??
                && this.isMode(GameMode.InSrv, GameMode.OnFoot, GameMode.Landed, GameMode.Flying, GameMode.InFighter)
                && !this.hidePlottersFromCombatSuits
                && this.status.Altitude < 4000;
        }

        #endregion

        #region Process and window handle stuff

        /// <summary>
        /// Returns True if the game process is actively running
        /// </summary>
        public bool isRunning
        {
            get
            {
                var procED = Process.GetProcessesByName("EliteDangerous64");
                return procED.Length > 0;
            }
        }

        #endregion

        #region start-up / initialization from journals

        private void initializeFromJournal(LoadGame? loadEntry = null)
        {
            log($"Game.initializeFromJournal: BEGIN {this.Commander} (FID:{this.fid}), journals.Count:{journals?.Count}");

            if (loadEntry == null)
                loadEntry = this.journals!.FindEntryByType<LoadGame>(-1, true);

            // read cmdr info
            if (loadEntry != null)
            {
                if (this.Commander == null) this.Commander = loadEntry.Commander;
                if (this.fid == null) this.fid = loadEntry.FID;

                Game.settings.lastCommander = this.Commander;
                Game.settings.lastFid = this.fid;
            }

            // exit early if we are shutdown
            var lastShutdown = journals!.FindEntryByType<Shutdown>(-1, true);
            if (lastShutdown != null)
            {
                log($"Game.initializeFromJournal: EXIT isShutdown! ({Game.activeGame == this})");
                this.isShutdown = true;
                return;
            }

            if (loadEntry != null)
                this.cmdr = CommanderSettings.Load(loadEntry.FID, loadEntry.Odyssey, loadEntry.Commander);

            // if we have MainMenu music - we know we're not actively playing
            var lastMusic = journals.FindEntryByType<Music>(-1, true);
            if (lastMusic != null)
                onJournalEntry(lastMusic);

            // try to find a location from recent journal items
            if (cmdr != null)
            {
                this.initSystemData();

                journals.walk(-1, true, (entry) =>
                {
                    var locationEvent = entry as Location;
                    if (locationEvent != null)
                    {
                        this.setLocations(locationEvent);
                        return true;
                    }

                    var jumpEvent = entry as FSDJump;
                    if (jumpEvent != null)
                    {
                        this.setLocations(jumpEvent);
                        return true;
                    }

                    var superCruiseExitEvent = entry as SupercruiseExit;
                    if (superCruiseExitEvent != null)
                    {
                        this.setLocations(superCruiseExitEvent);
                        return true;
                    }

                    var approachBodyEvent = entry as ApproachBody;
                    if (approachBodyEvent != null)
                    {
                        this.setLocations(approachBodyEvent);
                        return true;
                    }
                    return false;
                });
                log($"Game.initializeFromJournal: system: '{cmdr.currentSystem}' (id:{cmdr.currentSystemAddress}), body: '{cmdr.currentBody}' (id:{cmdr.currentBodyId}, r: {Util.metersToString(cmdr.currentBodyRadius)})");

                this.systemStatus = new SystemStatus(cmdr.currentSystem, cmdr.currentSystemAddress);
                this.systemStatus.initFromJournal(this);

            }

            // if we are near a planet
            if (this.nearBody == null && status.hasLatLong)
            {
                this.createNearBody(status.BodyName);
                //// do lat/long check
                //// are we near a guardian site?
                //journals.searchDeep<ApproachSettlement>((entry) =>
                //{
                //    if (entry != null && entry.SystemAddress == this.nearBody.systemAddress)
                //    {
                //        this.nearBody.onJournalEntry(entry);
                //        this.nearBody.settlements.Count;
                //        return true;
                //    }

                //    return false;
                //},
                //// search only as far as the FSDJump arrival
                //(JournalFile journals) => journals.search((FSDJump _) => true));

                //if (this.nearBody.siteData?.location != null && this.nearBody.siteData.location.Lat == 0)
                //{
                //    // no longer needed?
                //    var lastApproachSettlement = journals.FindEntryByType<ApproachSettlement>(-1, true);
                //    if (lastApproachSettlement != null && lastApproachSettlement.SystemAddress == this.nearBody.systemAddress)
                //    {
                //        this.nearBody.onJournalEntry(lastApproachSettlement);
                //        this.nearBody.guardianSiteCount++;
                //    }
                //}
            }

            // if we have landed, we need to find the last Touchdown location
            if (this.isLanded && this._touchdownLocation == null) // || (status.Flags & StatusFlags.HasLatLong) > 0)
            {
                if (this.nearBody == null)
                {
                    Game.log("Why is nearBody null?");
                    return;
                }

                if (this.mode == GameMode.Landed)
                {
                    var location = journals.FindEntryByType<Location>(-1, true);
                    if (location != null && location.SystemAddress == this.nearBody.systemAddress && location.BodyID == this.nearBody.bodyId)
                    {
                        Game.log($"LastTouchdown from Location: {location}");
                        this.touchdownLocation = location;
                    }
                }

                if (this.touchdownLocation == null)
                {
                    this.getLastTouchdownDeep();
                }
            }

            log($"Game.initializeFromJournal: END Commander:{this.Commander}, starSystem:{cmdr?.currentSystem}, systemLocation:{cmdr?.lastSystemLocation}, nearBody:{this.nearBody}, journals.Count:{journals.Count}");
            this.initialized = Game.activeGame == this && this.Commander != null;
            this.checkModeChange();
        }

        private void initSystemData()
        {
            // try to find a location from recent journal items
            if (cmdr == null || this.journals == null) return;

            log($"Game.initSystemData: rewind to last FSDJump");

            var playForwards = new List<JournalEntry>();

            Location? lastLocation = null;
            this.journals.walkDeep(-1, true, (entry) =>
            {
                // record last location event, in case ...
                var locationEvent = entry as Location;
                if (locationEvent != null && lastLocation == null)
                {
                    log($"Game.initSystemData: last location: '{locationEvent.StarSystem}' ({locationEvent.SystemAddress})");
                    lastLocation = locationEvent;
                    return false;
                }

                // .. we were on a carrier and it jumped when we not playing, hence no FSD or Carrier jump event. We can init from the Location event, once we see that journal entries are coming from some other system
                var systemAddressEntry = entry as ISystemAddress;
                if (systemAddressEntry != null && lastLocation != null && systemAddressEntry.SystemAddress != lastLocation.SystemAddress)
                {
                    log($"Game.initSystemData: Carrier jump since last session? Some SystemAddress ({systemAddressEntry.SystemAddress}) does not match current location ({lastLocation.SystemAddress})");
                    this.systemData = SystemData.From(lastLocation);
                    return true;
                }

                // init from FSD or Carrier jump event
                var starterEvent = entry as ISystemDataStarter;
                if (starterEvent != null && starterEvent.@event != nameof(Location))
                {
                    log($"Game.initSystemData: found last {starterEvent.@event}, to '{starterEvent.StarSystem}' ({starterEvent.SystemAddress})");
                    this.systemData = SystemData.From(starterEvent);
                    return true;
                }

                if (SystemData.journalEventTypes.Contains(entry.@event))
                {
                    playForwards.Add(entry);
                }

                return false;
            });
            if (this.systemData == null) throw new Exception("Why no systemData?");

            log($"Game.initSystemData: Processing {playForwards.Count} journal items forwards...");
            playForwards.Reverse();
            foreach (var entry in playForwards)
                this.systemData.Journals_onJournalEntry(entry);

            this.systemData.Save();
            log($"Game.initSystemData: complete '{this.systemData.name}' ({this.systemData.address}), bodyCount: {this.systemData.bodyCount}");
        }

        private void getLastTouchdownDeep()
        {
            Game.log($"getLastTouchdownDeep");

            if (this.touchdownLocation == null && journals != null)
            {
                journals.searchDeep(
                    (Touchdown lastTouchdown) =>
                    {
                        Game.log($"LastTouchdown from deep search: {lastTouchdown}");

                        if (lastTouchdown.Body == status!.BodyName)
                        {
                            onJournalEntry(lastTouchdown);
                            return true;
                        }
                        return false;
                    },
                    (JournalFile journals) =>
                    {
                        // stop searching older journal files if we see FSDJump
                        return journals.search((FSDJump _) => true);
                    }
                );
            }
        }

        #endregion

        private void createNearBody(string bodyName)
        {
            if (string.IsNullOrEmpty(this.fid)) return;
            Game.log($"in createNearBody: {bodyName}");

            // exit early if we already have that body
            if (this.nearBody != null && this.nearBody.bodyName == bodyName) return;

            // from Scan
            if (this.nearBody == null)
            {
                // look for a recent Scan event
                journals!.searchDeep(
                    (Scan scan) =>
                    {
                        if (scan.Bodyname == bodyName)
                        {
                            this.nearBody = new LandableBody(
                                this,
                                scan.StarSystem,
                                scan.Bodyname,
                                scan.BodyID,
                                scan.SystemAddress,
                                scan.Radius);
                            return true;
                        }
                        return false;
                    },
                    (JournalFile journals) =>
                    {
                        // stop searching older journal files if we see FSDJump
                        return journals.search((FSDJump _) =>
                        {
                            return true;
                        });
                    }
                );
            }

            // from ApproachBody
            if (this.nearBody == null)
            {
                // look for a recent ApproachBody event?
                journals!.searchDeep(
                    (ApproachBody approachBody) =>
                    {
                        if (approachBody.Body == bodyName && status!.BodyName == approachBody.Body && status.PlanetRadius > 0)
                        {
                            this.nearBody = new LandableBody(
                                this,
                                approachBody.StarSystem,
                                approachBody.Body,
                                approachBody.BodyID,
                                approachBody.SystemAddress,
                                status.PlanetRadius);
                            return true;
                        }
                        return false;
                    },
                    (JournalFile journals) =>
                    {
                        // stop searching older journal files if we see FSDJump
                        return journals.search((FSDJump _) =>
                        {
                            return true;
                        });
                    }
                    );
            }

            // look for recent Location event?
            if (this.nearBody == null)
            {
                journals!.search((Location locationEntry) =>
                {
                    if (locationEntry.Body == bodyName && status!.BodyName == locationEntry.Body && status.PlanetRadius > 0)
                    {
                        this.nearBody = new LandableBody(
                            this,
                            locationEntry.StarSystem,
                            locationEntry.Body,
                            locationEntry.BodyID,
                            locationEntry.SystemAddress,
                            status.PlanetRadius);
                        return true;
                    }
                    return false;
                });
            }

            // look for recent SupercruiseExit event?
            if (this.nearBody == null)
            {
                journals!.search((SupercruiseExit exitEvent) =>
                {
                    if (exitEvent.Body == bodyName && status!.BodyName == exitEvent.Body && status.PlanetRadius > 0)
                    {
                        this.nearBody = new LandableBody(
                            this,
                            exitEvent.Starsystem,
                            exitEvent.Body,
                            exitEvent.BodyID,
                            exitEvent.SystemAddress,
                            status.PlanetRadius);
                        return true;
                    }
                    return false;
                });
            }

            // use the status file?
            if (this.nearBody == null)
            {
                // look for a recent Scan event
                journals!.searchDeep(
                    (SAASignalsFound entry) =>
                    {
                        if (entry.BodyName == bodyName && this.cmdr.currentSystemAddress == entry.SystemAddress && this.status.BodyName == bodyName)
                        {
                            this.nearBody = new LandableBody(
                                this,
                                this.cmdr.currentSystem,
                                entry.BodyName,
                                entry.BodyID,
                                entry.SystemAddress,
                                this.status.PlanetRadius);
                            return true;
                        }
                        return false;
                    },
                    (JournalFile journals) =>
                    {
                        // stop searching older journal files if we see FSDJump
                        return journals.search((FSDJump _) =>
                        {
                            return true;
                        });
                    }
                );
                //// 
                //if (this.status.BodyName == bodyName)
                //{
                //    this.nearBody = new LandableBody(
                //        this,
                //        this.cmdr.currentSystem,
                //        bodyName,
                //        exitEvent.BodyID,
                //        exitEvent.SystemAddress,
                //        status.PlanetRadius);
                //}
                //log($"Failed to create any body for: {bodyName}");
            }

            if (this.nearBody == null)
            {
                log($"Failed to create any body for: {bodyName}");
            }
            else
            {
                this.setCurrentBody(this.nearBody.bodyId);
            }

            if (this.nearBody?.data.countOrganisms > 0 && this.systemBody?.organisms != null)
            {
                log($"Genuses ({this.systemBody!.bioSignalCount}): " + string.Join(",", this.systemBody.organisms.Select(_ => _.genusLocalized)));
            }

            //if (this.nearBody?.guardianSiteCount > 0)
            //{
            //    // do we have an ApproachSettlement for this site?
            //    this.journals!.searchDeep<ApproachSettlement>(
            //    (ApproachSettlement _) =>
            //    {
            //        if (_.BodyName == nearBody?.bodyName && _.Name.StartsWith("$Ancient:") && _.Latitude != 0)
            //        {
            //            this.nearBody.onJournalEntry(_);
            //            return true;
            //        }
            //        return false;
            //    },
            //    // search only as far as the FSDJump arrival
            //    (JournalFile journals) => journals.search((FSDJump _) => true));
            //}

            if (this.nearBody != null)
            {
                Game.log($"EVENT:nearingBody, at {this.nearBody?.bodyName}");

                if (this.nearingBody != null && this.nearBody != null)
                    this.nearingBody(this.nearBody);
            }
        }

        #region journal tracking for game state and modes

        private void Journals_onJournalEntry(JournalEntry entry, int index)
        {
            Game.log($"Game.event => {entry.@event}");
            this.onJournalEntry((dynamic)entry);

            if (this.systemData != null)
            {
                this.systemData.Journals_onJournalEntry(entry);
                // TODO: maybe not every time?
                this.systemData.Save();
            }
        }

        private void onJournalEntry(JournalEntry entry) { /* ignore */ }

        private void onJournalEntry(LoadGame entry)
        {
            if (this.Commander == null)
                this.initializeFromJournal();
        }

        private void onJournalEntry(Location entry)
        {
            // Happens when game has loaded after being at the main menu
            // Or player resurrects at a station

            //this.starSystem = entry.StarSystem;
            //this.systemLocation = Util.getLocationString(entry.StarSystem, entry.Body);

            // rely on previously tracked locations?
            Game.log($"Create nearBody now? For: {entry.Body}, has nearBody: {this.nearBody != null}, cmdr.lastBody: {cmdr.currentBody}");
            if (this.nearBody == null && status.hasLatLong && cmdr.currentBody != null)
            {
                this.createNearBody(cmdr.currentBody);
            }

            this.setLocations(entry);

            if (this._touchdownLocation == null && this.nearBody != null && this.isMode((GameMode.Landed | GameMode.InSrv | GameMode.OnFoot)))
            {
                // find the last touchdown location if needed
                this.getLastTouchdownDeep();
            }

            // start a new SystemStatus
            this.systemStatus = new SystemStatus(entry.StarSystem, entry.SystemAddress);
            this.systemStatus.initFromJournal(this);
        }

        private void onJournalEntry(Died entry)
        {
            Game.log($"You died. Clearing ${Util.credits(this.cmdr.organicRewards)} from {this.cmdr.scannedOrganics.Count} organisms.");
            // revisit all active bio-scan entries per body and mark them as Died
            /*
            this.cmdr.scannedOrganics.Clear();
            this.cmdr.scanOne = null;
            this.cmdr.scanTwo = null;
            this.cmdr.lastOrganicScan = null;
            // !! this.cmdr.Save();
            */
        }

        private void onJournalEntry(Music entry)
        {
            this.musicTrack = entry.MusicTrack;

            var newMainMenu = entry.MusicTrack == "MainMenu";
            if (this.atMainMenu != newMainMenu)
            {
                // if atMainMenu has changed - force a status change event
                this.atMainMenu = newMainMenu;
                this.Status_StatusChanged(false);
            }

            var newCarrierMgmt = entry.MusicTrack == "FleetCarrier_Managment";
            if (this.atCarrierMgmt != newCarrierMgmt)
            {
                // if atMainMenu has changed - force a status change event
                this.atCarrierMgmt = newCarrierMgmt;
                this.Status_StatusChanged(false);
            }

            if (entry.MusicTrack == "GuardianSites" && this.nearBody != null)
            {
                // Are we at a Guardian site without realizing?
                if (this.nearBody.siteData == null)
                    nearBody.findGuardianSites();

                if (this.showGuardianPlotters)
                    this.fireUpdate(true);
            }
        }

        private void onJournalEntry(Shutdown entry)
        {
            this.atMainMenu = false;
            this.isShutdown = true;
            this.checkModeChange();
            this.Status_StatusChanged(false);
        }

        private void onJournalEntry(StartJump entry)
        {
            // FSD charged - either for Jump or SuperCruise
            if (entry.JumpType == "Hyperspace")
            {
                this.fsdJumping = true;
                this.canonnPoi = null;
                this.systemBody = null;
                this.systemData = null;
                this.checkModeChange();
                this.Status_StatusChanged(false);

                // clear the nextSystem displayed when jumping to some other system
                var form = Program.getPlotter<PlotSysStatus>();
                if (form != null)
                {
                    form.nextSystem = null;
                    form.Invalidate();
                }
            }
        }

        private void onJournalEntry(FSDJump entry)
        {
            // FSD Jump completed
            this.fsdJumping = false;
            this.statusBodyName = null;

            this.setLocations(entry);

            // start a new SystemStatus
            this.systemStatus = new SystemStatus(entry.StarSystem, entry.SystemAddress);
            this.systemStatus.initFromJournal(this);

            this.checkModeChange();
        }

        private void onJournalEntry(SupercruiseEntry entry)
        {
            Program.closePlotter<PlotGuardians>();
        }

        private void onJournalEntry(SupercruiseExit entry)
        {
            this.setLocations(entry);

            // check there are any Guardian sites nearby
            if (this.status.hasLatLong && this.nearBody != null)
                this.nearBody.findGuardianSites();
        }

        private void onJournalEntry(FSSDiscoveryScan entry)
        {
            this.systemStatus.onJournalEntry(entry); // retire
        }

        private void onJournalEntry(Scan entry)
        {
            this.systemStatus.onJournalEntry(entry); // retire
        }

        private void onJournalEntry(SAAScanComplete entry)
        {
            this.systemStatus.onJournalEntry(entry); // retire

            this.setCurrentBody(entry.BodyID);
            this.fireUpdate();
        }

        private void onJournalEntry(FSSAllBodiesFound entry)
        {
            this.systemStatus.onJournalEntry(entry); // retire
        }

        private void onJournalEntry(FSSBodySignals entry)
        {
            this.systemStatus.onJournalEntry(entry); // retire
        }

        #endregion

        #region location tracking

        private void setCurrentBody(int bodyId)
        {
            log($"setCurrentBody: {bodyId}");
            if (this.systemData == null)
                throw new Exception($"Why no systemData for bodyId: {bodyId}?");
            this.systemBody = this.systemData.bodies.FirstOrDefault(_ => _.id == bodyId);
            Program.invalidateActivePlotters();
        }

        public void setLocations(FSDJump entry)
        {
            Game.log($"setLocations: from FSDJump: {entry.StarSystem} / {entry.Body} ({entry.BodyType})");

            cmdr.currentSystem = entry.StarSystem;
            cmdr.currentSystemAddress = entry.SystemAddress;
            cmdr.starPos = entry.StarPos;
            this.systemData = SystemData.From(entry);

            if (entry.BodyType == BodyType.Planet)
            {
                // would this ever happen?
                Game.log($"setLocations: FSDJump is a planet?!");

                cmdr.currentBody = entry.Body;
                cmdr.currentBodyId = entry.BodyID;
                this.setCurrentBody(entry.BodyID);

                // steal radius from status?
                if (Game.activeGame!.status.BodyName == entry.Body)
                    cmdr.currentBodyRadius = Game.activeGame.status.PlanetRadius;
                else
                {
                    Game.log($"Cannot find PlanetRadius from status file! Searching for last Scan event.");
                    cmdr.currentBodyRadius = this.findLastRadius(entry.Body);
                }
            }
            else
            {
                cmdr.currentBody = null;
                cmdr.currentBodyId = -1;
                cmdr.currentBodyRadius = -1;
                this.systemBody = null;
            }
            cmdr.lastSystemLocation = Util.getLocationString(entry.StarSystem, entry.Body);
            cmdr.Save();
            this.fireUpdate();

            if (Game.settings.autoLoadPriorScans)
                this.preparePriorScans(entry.StarSystem);
        }

        public void setLocations(ApproachBody entry)
        {
            Game.log($"setLocations: from ApproachBody: {entry.StarSystem} / {entry.Body}");

            cmdr.currentSystem = entry.StarSystem;
            cmdr.currentSystemAddress = entry.SystemAddress;

            cmdr.currentBody = entry.Body;
            cmdr.currentBodyId = entry.BodyID;
            this.setCurrentBody(entry.BodyID);

            // steal radius from status?
            if (Game.activeGame!.status.BodyName == entry.Body)
                cmdr.currentBodyRadius = Game.activeGame.status.PlanetRadius;
            else
            {
                Game.log($"Cannot find PlanetRadius from status file! Searching for last Scan event.");
                cmdr.currentBodyRadius = this.findLastRadius(entry.Body);
            }

            cmdr.lastSystemLocation = Util.getLocationString(entry.StarSystem, entry.Body);
            cmdr.Save();

            if (Game.settings.autoLoadPriorScans)
                this.preparePriorScans(entry.StarSystem);
        }

        public void setLocations(SupercruiseExit entry)
        {
            Game.log($"setLocations: from SupercruiseExit: {entry.Starsystem} / {entry.Body} ({entry.BodyType})");

            cmdr.currentSystem = entry.Starsystem;
            cmdr.currentSystemAddress = entry.SystemAddress;

            if (entry.BodyType == "Planet")
            {
                cmdr.currentBody = entry.Body;
                cmdr.currentBodyId = entry.BodyID;
                this.setCurrentBody(entry.BodyID);

                // steal radius from status?
                if (Game.activeGame!.status.BodyName == entry.Body)
                    cmdr.currentBodyRadius = Game.activeGame.status.PlanetRadius;
                else
                {
                    Game.log($"Cannot find PlanetRadius from status file! Searching for last Scan event.");
                    cmdr.currentBodyRadius = this.findLastRadius(entry.Body);
                }
            }
            else
            {
                cmdr.currentBody = null;
                cmdr.currentBodyId = -1;
                cmdr.currentBodyRadius = -1;
                this.systemBody = null;
            }

            cmdr.lastSystemLocation = Util.getLocationString(entry.Starsystem, entry.Body);
            cmdr.Save();

            if (Game.settings.autoLoadPriorScans)
                this.preparePriorScans(entry.Starsystem);
        }

        public void setLocations(Location entry)
        {
            Game.log($"setLocations: from Location: {entry.StarSystem} / {entry.Body} ({entry.BodyType})");

            cmdr.currentSystem = entry.StarSystem;
            cmdr.currentSystemAddress = entry.SystemAddress;
            cmdr.starPos = entry.StarPos;
            this.systemData = SystemData.From(entry);

            if (entry.BodyType == BodyType.Planet)
            {
                cmdr.currentBody = entry.Body;
                cmdr.currentBodyId = entry.BodyID;
                this.setCurrentBody(entry.BodyID);

                // steal radius from status?
                if (Game.activeGame!.status.BodyName == entry.Body)
                    cmdr.currentBodyRadius = Game.activeGame.status.PlanetRadius;
                else
                {
                    Game.log($"Cannot find PlanetRadius from status file! Searching for last Scan event.");
                    cmdr.currentBodyRadius = this.findLastRadius(entry.Body);
                }
            }
            else
            {
                cmdr.currentBody = null;
                cmdr.currentBodyId = -1;
                cmdr.currentBodyRadius = -1;
                this.systemBody = null;
            }

            cmdr.lastSystemLocation = Util.getLocationString(entry.StarSystem, entry.Body);
            cmdr.Save();

            if (Game.settings.autoLoadPriorScans)
                this.preparePriorScans(entry.StarSystem);
        }

        private decimal findLastRadius(string bodyName)
        {
            decimal planetRadius = -1;

            journals!.searchDeep(
                (Scan scan) =>
                {
                    if (scan.Bodyname == bodyName)
                    {
                        planetRadius = scan.Radius;
                        return true;
                    }
                    return false;
                },
                // search only as far as the FSDJump arrival
                (JournalFile journals) => journals.search((FSDJump _) => true)
            );

            return planetRadius;
        }

        private void preparePriorScans(string systemName)
        {
            if (this.canonnPoi?.system == systemName)
            {
                return;
            }
            else if (this.canonnPoi != null)
            {
                Game.log($"why/when {systemName} vs {this.canonnPoi.system}");
                this.canonnPoi = null;
            }

            // make a call for system POIs and pre-load trackers for known bio-signals
            Game.log($"Searching for system POI from Canonn...");
            Game.canonn.getSystemPoi(systemName).ContinueWith(response =>
            {
                this.canonnPoi = response.Result;
                Game.log($"Found system POI from Canonn for: {systemName}");
                this.showPriorScans();

                if (this.systemStatus != null)
                    this.systemStatus.mergeCanonnPoi(this.canonnPoi);
            });
        }

        public void showPriorScans()
        {
            if (!Game.settings.autoLoadPriorScans || this.canonnPoi == null || this.canonnPoi.codex == null || this.systemData == null || this.systemBody == null) return;

            Game.log($"Filtering organic signals from Canonn...");
            var currentBody = this.systemBody.name.Replace(this.systemData.name, "").Trim();
            var localPoi = this.canonnPoi.codex.Where(_ => _.body == currentBody && _.hud_category == "Biology" && _.latitude != null && _.longitude != null).ToList();
            Game.log($"Found {localPoi.Count} organic signals from Canonn for: {this.systemBody.name}");

            if (localPoi.Count > 0)
            {
                Program.control.Invoke(new Action(() =>
                {
                    var form = Program.showPlotter<PlotPriorScans>();
                    form.setPriorScans(localPoi);
                }));
            }

            // TODO: add some details to systemData too?
        }

        #endregion

        #region journal tracking for ground ops

        private LatLong2? _touchdownLocation;

        public LatLong2 touchdownLocation
        {
            get
            {
                if (this._touchdownLocation == null && this.journals != null)
                {
                    Game.log($"Searching journals for last touchdown location... (mode: {this.mode})");
                    journals.searchDeep(
                        (Touchdown entry) =>
                        {
                            _touchdownLocation = new LatLong2(entry);
                            Game.log($"Found last touchdown location: {_touchdownLocation}");
                            return true;
                        },
                        // stop searching older journal files if we see we reached this system
                        (JournalFile journals) => journals.search((ApproachBody _) => true)
                    );

                    /*
                    var lastTouchdown = journals!.FindEntryByType<Touchdown>(-1, true);
                    var lastLiftoff = journals.FindEntryByType<Liftoff>(-1, true);
                    if (lastTouchdown != null)
                    {
                        if (lastLiftoff == null || lastTouchdown.timestamp > lastLiftoff.timestamp)
                        {
                            _touchdownLocation = new LatLong2(lastTouchdown);
                            Game.log($"Found last touchdown location: {_touchdownLocation}");
                        }
                        else
                        {
                            // TODO: search deep for landing in prior journal file.
                            Game.log($"Last touchdown location: NOT FOUND");
                            //    _touchdownLocation = new LatLong2(lastTouchdown);
                        }
                    }
                    else if (this.mode == GameMode.Landed)
                    {
                        _touchdownLocation = Status.here.clone();
                        Game.log($"Last touchdown location: NOT FOUND but we're landed so using current location: {_touchdownLocation}");
                    }
                    */
                }
                return _touchdownLocation!;
            }
            set
            {
                this._touchdownLocation = value;
                if (this.nearBody != null) this.nearBody.data.lastTouchdown = value;
                if (this.systemBody != null) this.systemBody.lastTouchdown = value;
            }
        }

        private void onJournalEntry(Touchdown entry)
        {
            this.touchdownLocation = entry;

            var ago = Util.timeSpanToString(DateTime.UtcNow - entry.timestamp);
            log($"Touchdown {ago}, at: {touchdownLocation}");
        }

        private void onJournalEntry(Liftoff entry)
        {
            this._touchdownLocation = LatLong2.Empty;
            log($"Liftoff!");
        }

        private void onJournalEntry(ApproachBody entry)
        {
            this.setLocations(entry);

            if (this.nearBody == null)
                this.createNearBody(entry.Body);
        }

        private void onJournalEntry(ApproachSettlement entry)
        {
            if (this.nearBody == null)
                this.createNearBody(entry.BodyName);

            if (this.nearBody != null)
                this.nearBody.onJournalEntry(entry);
        }

        private void onJournalEntry(CodexEntry entry)
        {
            if (entry.Name == "$Codex_Ent_Guardian_Beacons_Name;")
            {
                // A Guardian Beacon
                Game.log($"Scanned Guardian Beacon in: {entry.System}");
                var data = GuardianBeaconData.Load(entry);

                // add the lat/long co-ordinates
                data.scannedLocations[DateTime.UtcNow] = entry;
                data.lastVisited = DateTime.UtcNow;
                data.Save();
            }
            else if (entry.Category == "$Codex_Category_Biology;" && Game.settings.autoTrackCompBioScans)
            {
                // auto add CodexScans as a tracker location
                var namePart = entry.Name.Split('_')[2];
                var match = BioScan.prefixes.FirstOrDefault(_ => _.Value.Contains(namePart, StringComparison.OrdinalIgnoreCase));
                var prefix = match.Key;
                var genusName = match.Value;
                if (prefix != null && this.systemBody?.organisms != null)
                {
                    // wait a bit for the status file to update
                    Application.DoEvents();
                    Game.log($"!! Comp scan organic: {genusName} ({entry.Name}) timestamps entry: {entry.timestamp} vs status: {this.status.timestamp} | Locations: entry: {entry.Latitude}, {entry.Longitude} vs status: {this.status.Latitude}, {this.status.Longitude}");
                    var organism = systemBody.organisms.FirstOrDefault(_ => _.genus == genusName);
                    if (organism?.analyzed == true && Game.settings.skipAnalyzedCompBioScans)
                    {
                        Game.log($"Already analyzed, NOT auto-adding tracker for: {genusName}");
                    }
                    else
                    {
                        // whilst CodexEntry has a lat/long ... it's further away than the cmdr's current location
                        // PlotTrackers.processCommand($"+{prefix}", entry); // TODO: retire
                        this.addBookmark(prefix, entry);
                        Program.showPlotter<PlotTrackers>().prepTrackers();
                        Game.log($"Auto-adding tracker from CodexEntry: {genusName} ({entry.Name})");
                    }
                }
                else
                {
                    Game.log($"Genus name '{genusName}' not found from: '{entry.Name}' ({entry.Name_Localised})");
                }
            }
        }

        private void onJournalEntry(SAASignalsFound entry)
        {
            if (this.nearBody == null)
                this.createNearBody(entry.BodyName);
            else
                this.nearBody.readSAASignalsFound(entry);

            if (this.systemStatus != null)
                this.systemStatus.onJournalEntry(entry);

            // force a mode change to update ux
            fireUpdate(this._mode, true);
        }

        private void onJournalEntry(ScanOrganic entry)
        {
            this.systemStatus.onJournalEntry(entry);

            // add to bio scan locations. Skip for ScanType == ScanType.Analyse as a Sample event happens right before at the same location
            if (entry.ScanType == ScanType.Analyse)
            {
                if (this.systemData == null || this.systemBody == null) throw new Exception("Why no systemBody?");
                if (this.systemBody.bioScans == null) this.systemBody.bioScans = new List<BioScan>();

                // add a new BioScan for this 3rd and final entry
                var bioScan = new BioScan
                {
                    location = Status.here.clone(),
                    genus = entry.Genus,
                    species = entry.Species,
                    radius = BioScan.ranges[entry.Genus],
                    status = BioScan.Status.Complete,
                };
                this.systemBody.bioScans.Add(bioScan);

                // and add the first two scans as completed
                if (this.cmdr.scanOne != null)
                {
                    // (this can happen if there are 2 copies of Elite running at the same time)
                    this.cmdr.scanOne!.status = BioScan.Status.Complete;
                    this.systemBody.bioScans.Add(this.cmdr.scanOne);
                    this.cmdr.scanTwo!.status = BioScan.Status.Complete;
                    this.systemBody.bioScans.Add(this.cmdr.scanTwo);
                }
            }

            // force a mode change to update ux
            fireUpdate(this._mode, true);
        }

        private void onJournalEntry(SellOrganicData entry)
        {
            // match and remove sold items from running totals
            Game.log($"SellOrganicData: removing {entry.BioData.Count} scans, worth: {Util.credits(entry.BioData.Sum(_ => _.Value))} + bonus: {Util.credits(entry.BioData.Sum(_ => _.Bonus))}");
            foreach (var data in entry.BioData)
            {
                var match = cmdr.scannedOrganics.Find(_ => _.species == data.Species && _.reward == data.Value);
                if (match == null)
                {
                    Game.log($"No match found when selling: {data.Species_Localised} for {data.Value} Cr");
                    continue;
                }

                cmdr.organicRewards -= data.Value;
                cmdr.scannedOrganics.Remove(match);
            }

            cmdr.Save();
            // force a mode change to update ux
            fireUpdate(this._mode, true);
        }

        private void setSuitType(SuitLoadout entry)
        {

            if (entry.SuitName.StartsWith("flightsuit"))
                this.currentSuitType = SuitType.flightSuite;
            else if (entry.SuitName.StartsWith("explorationsuit"))
                this.currentSuitType = SuitType.artiemis;
            else if (entry.SuitName.StartsWith("utilitysuit"))
                this.currentSuitType = SuitType.maverick;
            else if (entry.SuitName.StartsWith("tacticalsuit"))
                this.currentSuitType = SuitType.dominator;
            else
                Game.log($"Unexpected suit type: {entry.SuitName}");

            Game.log($"setSuitType: '{entry.SuitName}' => {this.currentSuitType}");
        }

        private void onJournalEntry(SuitLoadout entry)
        {
            this.setSuitType(entry);
        }

        private void onJournalEntry(SwitchSuitLoadout entry)
        {
            this.setSuitType(entry);
        }

        #endregion

        #region bookmarking

        public void addBookmark(string name, LatLong2 location)
        {
            if (this.systemData == null || this.systemBody == null)
                throw new Exception($"Why no systemData or systemBody?");


            if (this.systemBody.bookmarks == null) this.systemBody.bookmarks = new Dictionary<string, List<LatLong2>>();
            if (!this.systemBody.bookmarks.ContainsKey(name)) this.systemBody.bookmarks[name] = new List<LatLong2>();

            var pos = location;
            var tooClose = this.systemBody.bookmarks[name].Any(_ => Util.getDistance(_, location, this.systemBody.radius) < 20);

            if (tooClose)
            {
                Game.log($"Too close to prior bookmark. Move 20m away");
                return;
            }

            Game.log($"Add bookmark '{name}' ({location}) on '{this.systemBody.name}' ({this.systemBody.id}");
            this.systemBody.bookmarks[name].Add(pos);

            // TODO: limit to only 4?
            //Game.log($"Group '{name}' has too many entries. Ignoring location: {location}");
            this.systemData.Save();
        }

        public void clearAllBookmarks()
        {
            if (this.systemData == null || this.systemBody == null) return;
            Game.log($"Clearing all bookmarks on '{this.systemBody.name}' ({this.systemBody.id}");

            this.systemBody.bookmarks = null;
            this.systemData.Save();
        }

        public void removeBookmarkName(string name)
        {
            if (this.systemData == null || this.systemBody?.bookmarks == null) return;
            Game.log($"Clearing all bookmarks for '{name}' on '{this.systemBody.name}' ({this.systemBody.id}");
            if (!this.systemBody.bookmarks.ContainsKey(name)) return;

            this.systemBody.bookmarks.Remove(name);
            if (this.systemBody.bookmarks.Count == 0) this.systemBody.bookmarks = null;
            this.systemData.Save();
        }

        public void removeBookmark(string name, LatLong2 location, bool nearest)
        {
            if (this.systemData == null || this.systemBody == null)
                throw new Exception($"Why no systemData or systemBody?");
            if (this.systemBody.bookmarks == null || this.systemBody.bookmarks.Count == 0) return;

            if (!this.systemBody.bookmarks.ContainsKey(name)) return;

            var list = this.systemBody.bookmarks[name]
                .Select(_ => new TrackingDelta(this.systemBody.radius, _, location))
                .ToList();
            list.Sort((a, b) => a.distance.CompareTo(b.distance));

            var victim = list[nearest ? 0 : list.Count - 1].Target;

            Game.log($"Removing nearest bookmark '{name}' to {location}, victim: {victim} on '{this.systemBody.name}' ({this.systemBody.id}");
            this.systemBody.bookmarks[name].Remove(victim);

            if (this.systemBody.bookmarks[name].Count == 0) this.systemBody.bookmarks.Remove(name);
            if (this.systemBody.bookmarks.Count == 0) this.systemBody.bookmarks = null;

            this.systemData.Save();
        }

        #endregion

    }
}
