using System.Text;
using CodexTelegramCommon;

namespace CodexTelegramIntegrationTests;

public sealed class InstallApplierIntegrationTests
{
    [Fact]
    public void Applies_shadow_install_and_preserves_vendor_upstream()
    {
        using var fixture = new ApplyFixture();

        var result = fixture.CreateApplier().Apply(fixture.PlanPath);

        var layout = new InstallationLayout(fixture.InstallRoot);
        Assert.Equal(fixture.Plan.MachineId, result.MachineId);
        Assert.False(result.WasUpgrade);
        Assert.Equal([layout.Bin + Path.DirectorySeparatorChar + "CodexTelegramBridge.exe", "hook"],
            CodexConfigDocument.Parse(File.ReadAllBytes(fixture.ConfigPath)).NotifyArgv);
        var runtime = new RuntimeConfigStore(layout.RuntimeConfigPath).Load();
        Assert.Equal(CaptureMode.Shadow, runtime.CaptureMode);
        Assert.False(runtime.DeliveryPaused);
        var upstream = new ProtectedJsonStore<UpstreamRecord>(layout.UpstreamPath, fixture.Protector).Load();
        Assert.Equal(UpstreamKind.CodexComputerUseTurnEnded, upstream.Kind);
        Assert.Equal(fixture.VendorPath, upstream.Argv[0]);
        Assert.True(fixture.Tasks.Enabled);
        Assert.False(File.Exists(layout.ActiveJournalPath));
        Assert.True(PackageManifest.LoadAndVerify(layout.Root, allowInstalledMutableFiles: true).Entries.Count >= 2);
        Assert.True(WindowsAclManager.VerifyTree(layout.Root, CurrentUserContext.Sid).IsValid);
    }

    [Theory]
    [InlineData("PLAN_VALIDATED")]
    [InlineData("ROOT_READY")]
    [InlineData("STAGED")]
    [InlineData("BACKUP_READY")]
    [InlineData("CONFIG_PENDING")]
    [InlineData("CONFIG_COMMITTED")]
    [InlineData("TASKS_ENABLED")]
    [InlineData("COMMITTED")]
    public void Every_journal_phase_failure_restores_config_and_removes_new_install(string phase)
    {
        using var fixture = new ApplyFixture();
        var original = File.ReadAllBytes(fixture.ConfigPath);
        var applier = fixture.CreateApplier(current =>
        {
            if (string.Equals(current, phase, StringComparison.Ordinal))
            {
                throw new SimulatedFaultException();
            }
        });

        var error = Assert.Throws<InstallApplyException>(() => applier.Apply(fixture.PlanPath));

        Assert.True(error.RolledBack);
        Assert.Equal(original, File.ReadAllBytes(fixture.ConfigPath));
        Assert.False(Directory.Exists(fixture.InstallRoot));
        Assert.False(File.Exists(new InstallationLayout(fixture.InstallRoot).ActiveJournalPath));
        Assert.False(fixture.Tasks.Enabled);
    }

    [Fact]
    public void Apply_rejects_process_appearance_before_any_config_write()
    {
        using var fixture = new ApplyFixture();
        fixture.RunningProcesses = ["Codex"];
        var original = File.ReadAllBytes(fixture.ConfigPath);

        Assert.Throws<InvalidOperationException>(() => fixture.CreateApplier().Apply(fixture.PlanPath));

        Assert.Equal(original, File.ReadAllBytes(fixture.ConfigPath));
        Assert.False(Directory.Exists(fixture.InstallRoot));
    }

    [Fact]
    public void Apply_rejects_installation_identity_created_after_plan()
    {
        using var fixture = new ApplyFixture();
        var original = File.ReadAllBytes(fixture.ConfigPath);
        var layout = new InstallationLayout(fixture.InstallRoot);
        Directory.CreateDirectory(layout.ConfigDirectory);
        AtomicFile.WriteUtf8(layout.RuntimeConfigPath, "changed after planning");

        Assert.Throws<InvalidOperationException>(() => fixture.CreateApplier().Apply(fixture.PlanPath));

        Assert.Equal(original, File.ReadAllBytes(fixture.ConfigPath));
        Assert.False(File.Exists(layout.ActiveJournalPath));
    }

    [Theory]
    [InlineData("STAGED")]
    [InlineData("COMMITTED")]
    public void Upgrade_fault_restores_old_binaries_runtime_record_and_tasks_then_success_replaces_them(string faultPhase)
    {
        using var fixture = new ApplyFixture();
        fixture.CreateApplier().Apply(fixture.PlanPath);
        var layout = new InstallationLayout(fixture.InstallRoot);
        var bridgePath = Path.Combine(layout.Bin, "CodexTelegramBridge.exe");
        Assert.Equal("bridge-v1", File.ReadAllText(bridgePath));
        var (upgradePlan, upgradePlanPath) = fixture.CreateUpgradePlan("v2");
        Assert.Equal(NotifyClassification.HealthyBridge, upgradePlan.NotifyClassification);

        var failed = fixture.CreateApplier(phase =>
        {
            if (phase == faultPhase)
            {
                throw new SimulatedFaultException();
            }
        });
        var error = Assert.Throws<InstallApplyException>(() => failed.Apply(upgradePlanPath));

        Assert.True(error.RolledBack);
        Assert.Equal("bridge-v1", File.ReadAllText(bridgePath));
        Assert.True(fixture.Tasks.Enabled);
        Assert.False(new RuntimeConfigStore(layout.RuntimeConfigPath).Load().DeliveryPaused);
        var retainedRecord = System.Text.Json.JsonSerializer.Deserialize<InstallationRecord>(AtomicFile.ReadUtf8(layout.TransactionRecordPath), JsonDefaults.Options)!;
        Assert.Equal(Hashing.Sha256File(Path.Combine(layout.Root, "manifest.sha256")), retainedRecord.ManifestSha256);

        var result = fixture.CreateApplier().Apply(upgradePlanPath);
        Assert.True(result.WasUpgrade);
        Assert.Equal("bridge-v2", File.ReadAllText(bridgePath));
        Assert.True(fixture.Tasks.Enabled);
    }

    [Fact]
    public void Upgrade_recognizes_and_preserves_vendor_wrapped_bridge()
    {
        using var fixture = new ApplyFixture();
        fixture.CreateApplier().Apply(fixture.PlanPath);
        var layout = new InstallationLayout(fixture.InstallRoot);
        fixture.SetVendorWrappedBridge();

        var (upgradePlan, upgradePlanPath) = fixture.CreateUpgradePlan("wrapped-v2");
        Assert.Equal(NotifyClassification.HealthyBridge, upgradePlan.NotifyClassification);

        var result = fixture.CreateApplier().Apply(upgradePlanPath);

        Assert.True(result.WasUpgrade);
        var notify = CodexConfigDocument.Parse(File.ReadAllBytes(fixture.ConfigPath)).NotifyArgv;
        var match = BridgeNotifyCommand.Match(notify, Path.Combine(layout.Bin, "CodexTelegramBridge.exe"));
        Assert.Equal(BridgeNotifyShape.VendorWrapped, match.Shape);
        Assert.Equal([fixture.VendorPath, BridgeConstants.VendorArgument], match.OuterVendorArgv);
    }

    [Fact]
    public void Conditional_repair_adopts_refreshed_vendor_and_restores_bridge()
    {
        using var fixture = new ApplyFixture();
        fixture.CreateApplier().Apply(fixture.PlanPath);
        var layout = new InstallationLayout(fixture.InstallRoot);
        File.WriteAllText(fixture.VendorPath, "vendor-v2");
        var refreshed = CodexConfigDocument.Parse(File.ReadAllBytes(fixture.ConfigPath))
            .RenderWithNotify([fixture.VendorPath, BridgeConstants.VendorArgument]);
        File.WriteAllBytes(fixture.ConfigPath, refreshed);

        var result = fixture.CreateRepairService().Run(layout);

        Assert.Equal(RepairOutcome.Repaired, result.Outcome);
        Assert.True(InstallPlanner.IsExactBridgeArgv(
            CodexConfigDocument.Parse(File.ReadAllBytes(fixture.ConfigPath)).NotifyArgv!,
            Path.Combine(layout.Bin, "CodexTelegramBridge.exe")));
        var upstream = new ProtectedJsonStore<UpstreamRecord>(layout.UpstreamPath, fixture.Protector).Load();
        Assert.Equal(Hashing.Sha256File(fixture.VendorPath), upstream.ExecutableSha256);
        Assert.False(new RuntimeConfigStore(layout.RuntimeConfigPath).Load().DeliveryPaused);
        Assert.False(File.Exists(layout.ActiveJournalPath));
    }

    [Fact]
    public void Conditional_repair_refreshes_wrapped_vendor_without_rewriting_config()
    {
        using var fixture = new ApplyFixture();
        fixture.CreateApplier().Apply(fixture.PlanPath);
        var layout = new InstallationLayout(fixture.InstallRoot);
        File.WriteAllText(fixture.VendorPath, "vendor-v2");
        var wrapped = fixture.SetVendorWrappedBridge();

        var result = fixture.CreateRepairService().Run(layout);

        Assert.Equal(RepairOutcome.Repaired, result.Outcome);
        Assert.Equal(wrapped, File.ReadAllBytes(fixture.ConfigPath));
        var upstream = new ProtectedJsonStore<UpstreamRecord>(layout.UpstreamPath, fixture.Protector).Load();
        Assert.Equal(Hashing.Sha256File(fixture.VendorPath), upstream.ExecutableSha256);
        Assert.False(new RuntimeConfigStore(layout.RuntimeConfigPath).Load().DeliveryPaused);
        Assert.False(File.Exists(layout.ActiveJournalPath));
    }

    [Fact]
    public void Conditional_repair_never_overwrites_custom_handler()
    {
        using var fixture = new ApplyFixture();
        fixture.CreateApplier().Apply(fixture.PlanPath);
        var layout = new InstallationLayout(fixture.InstallRoot);
        var custom = Path.Combine(fixture.VendorRoot, "custom.exe");
        var divergent = CodexConfigDocument.Parse(File.ReadAllBytes(fixture.ConfigPath))
            .RenderWithNotify([custom, "private-argument"]);
        File.WriteAllBytes(fixture.ConfigPath, divergent);

        var result = fixture.CreateRepairService().Run(layout);

        Assert.Equal(RepairOutcome.Conflict, result.Outcome);
        Assert.Equal(divergent, File.ReadAllBytes(fixture.ConfigPath));
        Assert.Contains(new QueueStore(layout.DatabasePath).GetHealthConditions(), item => item.ConditionCode == HealthCodes.ConfigConflict);
    }

    [Theory]
    [InlineData("REPAIR_PLANNED")]
    [InlineData("UPSTREAM_CAPTURED")]
    [InlineData("CONFIG_PENDING")]
    [InlineData("CONFIG_COMMITTED")]
    [InlineData("COMMITTED")]
    public void Repair_phase_fault_restores_vendor_config_runtime_and_upstream(string phase)
    {
        using var fixture = new ApplyFixture();
        fixture.CreateApplier().Apply(fixture.PlanPath);
        var layout = new InstallationLayout(fixture.InstallRoot);
        var oldUpstream = File.ReadAllBytes(layout.UpstreamPath);
        var vendorConfig = CodexConfigDocument.Parse(File.ReadAllBytes(fixture.ConfigPath))
            .RenderWithNotify([fixture.VendorPath, BridgeConstants.VendorArgument]);
        File.WriteAllBytes(fixture.ConfigPath, vendorConfig);

        var result = fixture.CreateRepairService(current =>
        {
            if (current == phase)
            {
                throw new SimulatedFaultException();
            }
        }).Run(layout);

        Assert.Equal(RepairOutcome.Blocked, result.Outcome);
        Assert.Equal(vendorConfig, File.ReadAllBytes(fixture.ConfigPath));
        Assert.Equal(oldUpstream, File.ReadAllBytes(layout.UpstreamPath));
        Assert.False(new RuntimeConfigStore(layout.RuntimeConfigPath).Load().DeliveryPaused);
        Assert.False(File.Exists(layout.ActiveJournalPath));
    }

    [Fact]
    public void Uninstall_restores_latest_vendor_and_preserves_mutable_state_for_cleanup()
    {
        using var fixture = new ApplyFixture();
        fixture.CreateApplier().Apply(fixture.PlanPath);
        var layout = new InstallationLayout(fixture.InstallRoot);

        var result = fixture.CreateUninstallService().Run(layout, keepConfigConflict: false, purgeState: false);

        Assert.Equal(UninstallOutcome.CleanupRequired, result.Outcome);
        Assert.Equal([fixture.VendorPath, BridgeConstants.VendorArgument],
            CodexConfigDocument.Parse(File.ReadAllBytes(fixture.ConfigPath)).NotifyArgv);
        Assert.False(fixture.Tasks.Staged);
        var request = new ProtectedJsonStore<CleanupRequest>(result.CleanupRequestPath!, fixture.Protector).Load();
        Assert.False(request.PurgeState);
        Assert.Contains("bin/CodexTelegramBridge.exe", request.ManifestEntries);
        Assert.True(File.Exists(layout.DatabasePath));
        var record = System.Text.Json.JsonSerializer.Deserialize<InstallationRecord>(
            AtomicFile.ReadUtf8(layout.TransactionRecordPath), JsonDefaults.Options)!;
        Assert.Equal(InstallationState.RolledBack, record.State);
    }

    [Fact]
    public void Uninstall_removes_nested_bridge_from_vendor_wrapper()
    {
        using var fixture = new ApplyFixture();
        fixture.CreateApplier().Apply(fixture.PlanPath);
        var layout = new InstallationLayout(fixture.InstallRoot);
        fixture.SetVendorWrappedBridge();

        var result = fixture.CreateUninstallService().Run(layout, keepConfigConflict: false, purgeState: false);

        Assert.Equal(UninstallOutcome.CleanupRequired, result.Outcome);
        Assert.Equal(
            [fixture.VendorPath, BridgeConstants.VendorArgument],
            CodexConfigDocument.Parse(File.ReadAllBytes(fixture.ConfigPath)).NotifyArgv);
    }

    [Fact]
    public void Uninstall_restores_absent_notify_without_fabricating_handler()
    {
        using var fixture = new ApplyFixture();
        fixture.CreateApplier().Apply(fixture.PlanPath);
        var layout = new InstallationLayout(fixture.InstallRoot);
        new ProtectedJsonStore<UpstreamRecord>(layout.UpstreamPath, fixture.Protector).Save(new UpstreamRecord(
            BridgeConstants.SchemaVersion,
            [],
            null,
            null,
            DateTimeOffset.UtcNow,
            Hashing.Sha256File(fixture.ConfigPath),
            UpstreamKind.Absent));

        fixture.CreateUninstallService().Run(layout, keepConfigConflict: false, purgeState: false);

        Assert.Null(CodexConfigDocument.Parse(File.ReadAllBytes(fixture.ConfigPath)).NotifyArgv);
    }

    [Fact]
    public void Uninstall_conflict_requires_explicit_keep_and_never_changes_custom_config()
    {
        using var fixture = new ApplyFixture();
        fixture.CreateApplier().Apply(fixture.PlanPath);
        var layout = new InstallationLayout(fixture.InstallRoot);
        var custom = CodexConfigDocument.Parse(File.ReadAllBytes(fixture.ConfigPath))
            .RenderWithNotify([Path.Combine(fixture.VendorRoot, "custom.exe"), "private"]);
        File.WriteAllBytes(fixture.ConfigPath, custom);

        var blocked = fixture.CreateUninstallService().Run(layout, keepConfigConflict: false, purgeState: false);
        Assert.Equal(UninstallOutcome.Conflict, blocked.Outcome);
        Assert.Equal(custom, File.ReadAllBytes(fixture.ConfigPath));
        Assert.True(fixture.Tasks.Enabled);

        var allowed = fixture.CreateUninstallService().Run(layout, keepConfigConflict: true, purgeState: false);
        Assert.Equal(UninstallOutcome.CleanupRequired, allowed.Outcome);
        Assert.Equal(custom, File.ReadAllBytes(fixture.ConfigPath));
        Assert.False(fixture.Tasks.Staged);
    }

    [Fact]
    public void Uninstall_never_leaves_bridge_referenced_by_unsupported_wrapper()
    {
        using var fixture = new ApplyFixture();
        fixture.CreateApplier().Apply(fixture.PlanPath);
        var layout = new InstallationLayout(fixture.InstallRoot);
        var bridge = Path.Combine(layout.Bin, "CodexTelegramBridge.exe");
        var previous = System.Text.Json.JsonSerializer.Serialize(new[] { bridge, "hook" });
        var unsupported = CodexConfigDocument.Parse(File.ReadAllBytes(fixture.ConfigPath)).RenderWithNotify(
            [fixture.VendorPath, BridgeConstants.VendorArgument, "--unsupported", previous]);
        File.WriteAllBytes(fixture.ConfigPath, unsupported);

        var result = fixture.CreateUninstallService().Run(layout, keepConfigConflict: true, purgeState: false);

        Assert.Equal(UninstallOutcome.Conflict, result.Outcome);
        Assert.Equal(unsupported, File.ReadAllBytes(fixture.ConfigPath));
        Assert.True(fixture.Tasks.Enabled);
    }

    [Fact]
    public void Uninstall_process_race_compensates_to_bridge_and_reenables_tasks()
    {
        using var fixture = new ApplyFixture();
        fixture.CreateApplier().Apply(fixture.PlanPath);
        var layout = new InstallationLayout(fixture.InstallRoot);
        var calls = 0;
        var service = fixture.CreateUninstallService(() => ++calls >= 3 ? ["Codex"] : []);

        Assert.Throws<InvalidOperationException>(() => service.Run(layout, keepConfigConflict: false, purgeState: false));

        Assert.True(InstallPlanner.IsExactBridgeArgv(
            CodexConfigDocument.Parse(File.ReadAllBytes(fixture.ConfigPath)).NotifyArgv!,
            Path.Combine(layout.Bin, "CodexTelegramBridge.exe")));
        Assert.True(fixture.Tasks.Enabled);
        Assert.False(new RuntimeConfigStore(layout.RuntimeConfigPath).Load().DeliveryPaused);
    }

    [Fact]
    public async Task Doctor_is_ok_for_shadow_install_and_never_serializes_credentials_or_titles()
    {
        using var fixture = new ApplyFixture();
        fixture.CreateApplier().Apply(fixture.PlanPath);
        var layout = new InstallationLayout(fixture.InstallRoot);
        new ProtectedJsonStore<TelegramCredentials>(layout.TelegramCredentialsPath, fixture.Protector)
            .Save(new TelegramCredentials(BridgeConstants.SchemaVersion, "123:must-never-appear", 42, 777, "private"));
        var service = new DoctorService(
            fixture.Protector,
            new VendorExecutableValidator(fixture.VendorRoot),
            fixture.Tasks,
            _ => throw new InvalidOperationException("Online client should not be created."),
            () => DateTimeOffset.UtcNow,
            () => CurrentUserContext.Sid,
            () => fixture.CodexHome);

        var report = await service.RunAsync(layout, online: false, CancellationToken.None);
        var json = System.Text.Json.JsonSerializer.Serialize(report, JsonDefaults.Options);

        Assert.Equal("OK", report.Overall);
        Assert.Empty(report.Conditions);
        Assert.Equal("COMPATIBLE_OR_ABSENT", report.Checks["codex_title_index"]);
        Assert.DoesNotContain("must-never-appear", json, StringComparison.Ordinal);
        Assert.DoesNotContain(fixture.VendorPath, json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Doctor_accepts_verified_vendor_wrapped_bridge()
    {
        using var fixture = new ApplyFixture();
        fixture.CreateApplier().Apply(fixture.PlanPath);
        var layout = new InstallationLayout(fixture.InstallRoot);
        fixture.SetVendorWrappedBridge();
        var service = new DoctorService(
            fixture.Protector,
            new VendorExecutableValidator(fixture.VendorRoot),
            fixture.Tasks,
            _ => throw new InvalidOperationException("Online client should not be created."),
            () => DateTimeOffset.UtcNow,
            () => CurrentUserContext.Sid,
            () => fixture.CodexHome);

        var report = await service.RunAsync(layout, online: false, CancellationToken.None);

        Assert.Equal("OK", report.Overall);
        Assert.Empty(report.Conditions);
        Assert.Equal("BRIDGE_ACTIVE_WRAPPED", report.Checks["codex_notify"]);
    }

    [Fact]
    public async Task Planner_and_doctor_reject_wrapped_bridge_with_untrusted_outer_vendor()
    {
        using var fixture = new ApplyFixture();
        fixture.CreateApplier().Apply(fixture.PlanPath);
        var layout = new InstallationLayout(fixture.InstallRoot);
        var outside = Path.Combine(Path.GetDirectoryName(fixture.VendorRoot)!, "outside-vendor", BridgeConstants.VendorExecutableName);
        Directory.CreateDirectory(Path.GetDirectoryName(outside)!);
        File.WriteAllText(outside, "untrusted");
        fixture.SetVendorWrappedBridge(outside);

        var (upgradePlan, _) = fixture.CreateUpgradePlan("untrusted-wrapper");
        var service = new DoctorService(
            fixture.Protector,
            new VendorExecutableValidator(fixture.VendorRoot),
            fixture.Tasks,
            _ => throw new InvalidOperationException(),
            () => DateTimeOffset.UtcNow,
            () => CurrentUserContext.Sid,
            () => fixture.CodexHome);
        var report = await service.RunAsync(layout, online: false, CancellationToken.None);

        Assert.Equal(NotifyClassification.Conflict, upgradePlan.NotifyClassification);
        Assert.Equal("CONFIG_CONFLICT", report.Checks["codex_notify"]);
        Assert.Contains(HealthCodes.ConfigConflict, report.Conditions);
    }

    [Fact]
    public void Live_gate_accepts_verified_vendor_wrapped_bridge()
    {
        using var fixture = new ApplyFixture();
        fixture.CreateApplier().Apply(fixture.PlanPath);
        var layout = new InstallationLayout(fixture.InstallRoot);
        fixture.SetVendorWrappedBridge();
        new ProtectedJsonStore<TelegramCredentials>(layout.TelegramCredentialsPath, fixture.Protector)
            .Save(new TelegramCredentials(BridgeConstants.SchemaVersion, "123:test", 42, 777, "private"));
        var control = new CaptureControl(
            fixture.Protector,
            _ => { },
            () => { },
            () => CurrentUserContext.Sid,
            new VendorExecutableValidator(fixture.VendorRoot),
            () => DateTimeOffset.UtcNow);

        control.EnableLive(layout);

        Assert.Equal(CaptureMode.Live, new RuntimeConfigStore(layout.RuntimeConfigPath).Load().CaptureMode);
    }

    [Fact]
    public async Task Doctor_reports_all_independent_conditions_instead_of_hiding_lower_priority_ones()
    {
        using var fixture = new ApplyFixture();
        fixture.CreateApplier().Apply(fixture.PlanPath);
        var layout = new InstallationLayout(fixture.InstallRoot);
        var queue = new QueueStore(layout.DatabasePath);
        queue.UpsertHealth(HealthCodes.AuthBlocked, DateTimeOffset.UtcNow);
        queue.UpsertHealth(HealthCodes.TelegramRetrying, DateTimeOffset.UtcNow);
        var custom = CodexConfigDocument.Parse(File.ReadAllBytes(fixture.ConfigPath))
            .RenderWithNotify([Path.Combine(fixture.VendorRoot, "custom.exe"), "private"]);
        File.WriteAllBytes(fixture.ConfigPath, custom);
        var service = new DoctorService(
            fixture.Protector,
            new VendorExecutableValidator(fixture.VendorRoot),
            fixture.Tasks,
            _ => throw new InvalidOperationException(),
            () => DateTimeOffset.UtcNow,
            () => CurrentUserContext.Sid,
            () => fixture.CodexHome);

        var report = await service.RunAsync(layout, online: false, CancellationToken.None);

        Assert.Equal("BLOCKED", report.Overall);
        Assert.Contains(HealthCodes.AuthBlocked, report.Conditions);
        Assert.Contains(HealthCodes.TelegramRetrying, report.Conditions);
        Assert.Contains(HealthCodes.ConfigConflict, report.Conditions);
    }

    private sealed class ApplyFixture : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "InstallApplierTests", Guid.NewGuid().ToString("N"));
        private readonly DateTimeOffset now = new(2026, 7, 11, 1, 0, 0, TimeSpan.Zero);

        public ApplyFixture()
        {
            PackageRoot = Path.Combine(root, "package");
            CodexHome = Path.Combine(root, "codex-home");
            InstallRoot = Path.Combine(root, "CodexTelegramBridge");
            VendorRoot = Path.Combine(root, "vendor");
            ConfigPath = Path.Combine(CodexHome, "config.toml");
            PlanPath = Path.Combine(root, "install-plan.json");
            Directory.CreateDirectory(Path.Combine(PackageRoot, "bin"));
            Directory.CreateDirectory(CodexHome);
            Directory.CreateDirectory(VendorRoot);
            File.WriteAllText(Path.Combine(PackageRoot, "bin", "CodexTelegramBridge.exe"), "bridge-v1");
            File.WriteAllText(Path.Combine(PackageRoot, "bin", "CodexTelegramCtl.exe"), "ctl-v1");
            WriteManifest(PackageRoot);
            VendorPath = Path.Combine(VendorRoot, BridgeConstants.VendorExecutableName);
            File.WriteAllText(VendorPath, "vendor-v1");
            File.WriteAllText(ConfigPath, $"# preserve\r\nnotify = [{Quote(VendorPath)}, \"turn-ended\"]\r\nmodel = \"gpt\"\r\n");
            using (var state = new Microsoft.Data.Sqlite.SqliteConnection(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
            {
                DataSource = Path.Combine(CodexHome, "state_5.sqlite"),
                Pooling = false,
            }.ToString()))
            {
                state.Open();
                using var command = state.CreateCommand();
                command.CommandText = "CREATE TABLE threads (id TEXT PRIMARY KEY, title TEXT, source TEXT, thread_source TEXT);";
                command.ExecuteNonQuery();
            }

            var planner = new InstallPlanner(
                new VendorExecutableValidator(VendorRoot),
                Protector,
                () => now,
                () => CurrentUserContext.Sid,
                () => [],
                () => { });
            Plan = planner.Create(new InstallPlannerOptions(PackageRoot, InstallRoot, CodexHome, "Test PC"));
            InstallPlanStore.Save(PlanPath, Plan);
        }

        public string PackageRoot { get; }
        public string CodexHome { get; }
        public string InstallRoot { get; }
        public string VendorRoot { get; }
        public string VendorPath { get; }
        public string ConfigPath { get; }
        public string PlanPath { get; }
        public InstallPlan Plan { get; }
        public ReversingProtector Protector { get; } = new();
        public FakeTasks Tasks { get; } = new();
        public IReadOnlyList<string> RunningProcesses { get; set; } = [];

        public InstallApplier CreateApplier(Action<string>? fault = null) => new(
            new VendorExecutableValidator(VendorRoot),
            Protector,
            Tasks,
            () => now,
            () => CurrentUserContext.Sid,
            () => CodexHome,
            () => RunningProcesses,
            _ => { },
            (_, _, _) => true,
            () => { },
            fault ?? (_ => { }));

        public RepairService CreateRepairService(Action<string>? fault = null) => new(
            Protector,
            new VendorExecutableValidator(VendorRoot),
            () => now,
            () => CurrentUserContext.Sid,
            () => RunningProcesses,
            _ => { },
            fault ?? (_ => { }));

        public UninstallService CreateUninstallService(Func<IReadOnlyList<string>>? processes = null) => new(
            Protector,
            new VendorExecutableValidator(VendorRoot),
            Tasks,
            () => now,
            () => CurrentUserContext.Sid,
            processes ?? (() => RunningProcesses),
            _ => { },
            (_, _, _) => true);

        public byte[] SetVendorWrappedBridge(string? outerVendorPath = null)
        {
            var layout = new InstallationLayout(InstallRoot);
            var bridge = Path.Combine(layout.Bin, "CodexTelegramBridge.exe");
            var previous = System.Text.Json.JsonSerializer.Serialize(new[] { bridge, "hook" });
            var wrapped = CodexConfigDocument.Parse(File.ReadAllBytes(ConfigPath)).RenderWithNotify(
                [outerVendorPath ?? VendorPath, BridgeConstants.VendorArgument, BridgeConstants.VendorPreviousNotifyArgument, previous]);
            File.WriteAllBytes(ConfigPath, wrapped);
            return wrapped;
        }

        public (InstallPlan Plan, string Path) CreateUpgradePlan(string suffix)
        {
            var package = Path.Combine(root, $"package-{suffix}");
            Directory.CreateDirectory(Path.Combine(package, "bin"));
            File.WriteAllText(Path.Combine(package, "bin", "CodexTelegramBridge.exe"), $"bridge-{suffix}");
            File.WriteAllText(Path.Combine(package, "bin", "CodexTelegramCtl.exe"), $"ctl-{suffix}");
            WriteManifest(package);
            var planner = new InstallPlanner(
                new VendorExecutableValidator(VendorRoot),
                Protector,
                () => now,
                () => CurrentUserContext.Sid,
                () => [],
                () => { });
            var plan = planner.Create(new InstallPlannerOptions(package, InstallRoot, CodexHome));
            var path = Path.Combine(root, $"upgrade-{suffix}.json");
            InstallPlanStore.Save(path, plan);
            return (plan, path);
        }

        public void Dispose()
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            var journal = new InstallationLayout(InstallRoot).ActiveJournalPath;
            if (File.Exists(journal))
            {
                File.Delete(journal);
            }

            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }

        private static void WriteManifest(string package)
        {
            var relative = new[] { "bin/CodexTelegramBridge.exe", "bin/CodexTelegramCtl.exe" };
            var lines = relative.Select(path => $"{Hashing.Sha256File(Path.Combine(package, path.Replace('/', Path.DirectorySeparatorChar)))}  {path}");
            File.WriteAllText(Path.Combine(package, "manifest.sha256"), string.Join("\n", lines) + "\n", new UTF8Encoding(false));
        }

        private static string Quote(string value) => System.Text.Json.JsonSerializer.Serialize(value);
    }

    private sealed class FakeTasks : IScheduledTaskManager
    {
        public bool Staged { get; private set; }
        public bool Enabled { get; private set; }

        public void StageDisabled(InstallationLayout layout, string userSid, DateTimeOffset nowUtc)
        {
            Staged = true;
            Enabled = false;
        }

        public void EnableAll()
        {
            Assert.True(Staged);
            Enabled = true;
        }

        public void DisableAll() => Enabled = false;

        public void RemoveAll()
        {
            Enabled = false;
            Staged = false;
        }

        public IReadOnlyList<ScheduledTaskStatus> GetStatuses() =>
        [
            new("Drain", Staged, Enabled, 0, 3),
            new("Repair", Staged, Enabled, 0, 3),
        ];
    }

    public sealed class ReversingProtector : ISecretProtector
    {
        public byte[] Protect(ReadOnlySpan<byte> plaintext)
        {
            var bytes = plaintext.ToArray();
            Array.Reverse(bytes);
            return bytes;
        }

        public byte[] Unprotect(ReadOnlySpan<byte> ciphertext) => Protect(ciphertext);
    }

    private sealed class SimulatedFaultException : Exception;
}
