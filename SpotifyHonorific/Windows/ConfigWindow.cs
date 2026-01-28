using Dalamud.Interface.Colors;
using Dalamud.Interface.Windowing;
using Dalamud.Utility;
using SpotifyHonorific.Activities;
using SpotifyHonorific.Updaters;
using SpotifyHonorific.Utils;
using Dalamud.Bindings.ImGui;
using System.Numerics;
using Scriban;
using Scriban.Helpers;
using SpotifyAPI.Web.Auth;
using SpotifyAPI.Web;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using System.Collections.Generic;

namespace SpotifyHonorific.Windows;

public class ConfigWindow : Window
{
    private const int SPOTIFY_AUTH_TIMEOUT_MINUTES = 1;
    private const int SPOTIFY_CLIENT_ID_MAX_LENGTH = 100;
    private const int SPOTIFY_CLIENT_SECRET_MAX_LENGTH = 100;
    private const ushort MAX_INPUT_LENGTH = ushort.MaxValue;

    private Config Config { get; init; }
    private ImGuiHelper ImGuiHelper { get; init; }
    private Updater Updater { get; init; }

    private string _spotifyClientIdBuffer = string.Empty;
    private string _spotifyClientSecretBuffer = string.Empty;
    private static PKCECallbackActivator? SpotifyAuthServer;

    private string _lastAuthTimeString = string.Empty;
    private DateTime _cachedAuthTime;
    private string? _newlyCreatedTabName;
    private string[] _cachedConfigNames = [];
    private int _cachedConfigCount;

    private static readonly string RecreateText = "Recreate Defaults";
    private static readonly System.Reflection.PropertyInfo[] UpdaterContextProperties = typeof(UpdaterContext).GetProperties();

    public ConfigWindow(Config config, ImGuiHelper imGuiHelper, Updater updater) : base("Spotify Activity Honorific Config##configWindow")
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new(760, 420),
            MaximumSize = new(float.MaxValue, float.MaxValue)
        };

        Config = config;
        ImGuiHelper = imGuiHelper;
        Updater = updater;

        _spotifyClientIdBuffer = Config.SpotifyClientId;
        _spotifyClientSecretBuffer = Config.SpotifyClientSecret;
    }

    public override void Draw()
    {
        DrawMainSettings();
        ImGui.Separator();
        DrawSpotifySetup();
        ImGui.Separator();
        DrawValidationErrors();
        ImGui.Spacing();
        DrawActivityConfigTabs();
    }

    private void DrawValidationErrors()
    {
        if (Config.Validate(out var errors))
        {
            return; // No errors, don't display anything
        }

        ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.DalamudRed);
        ImGui.TextWrapped("⚠ Configuration Issues:");
        ImGui.PopStyleColor();

        ImGui.Indent(10);
        foreach (var error in errors)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.DalamudOrange);
            ImGui.TextWrapped($"• {error}");
            ImGui.PopStyleColor();
        }
        ImGui.Unindent(10);
        ImGui.Separator();
    }

    private void DrawMainSettings()
    {
        var enabled = Config.Enabled;
        if (ImGui.Checkbox("Enabled##enabled", ref enabled))
        {
            Config.Enabled = enabled;
            Config.Save();
        }

        ImGui.SameLine();
        ImGui.TextDisabled("(?)");
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Stops polling Spotify when your character has the <AFK> status.\nPolling will resume at a slower rate when you return.");
        }

        ImGui.SameLine();
        var enableDebugLogging = Config.EnableDebugLogging;
        if (ImGui.Checkbox("Debug Logging##debugLogging", ref enableDebugLogging))
        {
            Config.EnableDebugLogging = enableDebugLogging;
            Config.Save();
        }
        ImGui.SameLine();
        ImGui.TextDisabled("(?)");
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Prints detailed status information to the FFXIV plugin log (open with /xllog).\nThis is very spammy and should be kept off unless you are debugging.");
        }

        ImGui.Spacing();
        DrawActiveConfigSelector();
    }

    private void DrawActiveConfigSelector()
    {
        if (Config.ActivityConfigs.Count == 0)
        {
            ImGui.TextColored(ImGuiColors.DalamudRed, "No configs available. Create one below.");
            return;
        }

        ImGui.Text("Active Config:");
        ImGui.SameLine();

        if (_cachedConfigCount != Config.ActivityConfigs.Count)
        {
            _cachedConfigNames = new string[Config.ActivityConfigs.Count];
            for (var i = 0; i < Config.ActivityConfigs.Count; i++)
            {
                var name = Config.ActivityConfigs[i].Name;
                _cachedConfigNames[i] = string.IsNullOrWhiteSpace(name) ? $"(Blank #{i + 1})" : name;
            }
            _cachedConfigCount = Config.ActivityConfigs.Count;
        }

        var currentIndex = 0;
        for (var i = 0; i < Config.ActivityConfigs.Count; i++)
        {
            if (Config.ActivityConfigs[i].Name == Config.ActiveConfigName)
            {
                currentIndex = i;
                break;
            }
        }

        if (ImGui.Combo("##activeConfig", ref currentIndex, _cachedConfigNames, _cachedConfigNames.Length))
        {
            Config.ActiveConfigName = Config.ActivityConfigs[currentIndex].Name;
            Config.Save();
        }

        ImGui.SameLine();
        ImGui.TextDisabled("(?)");
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Select which config template to use when Spotify is playing.\nCreate additional configs in the tabs below for different styles.");
        }
    }

    private void DrawSpotifySetup()
    {
        ImGui.Text("Spotify Setup");

        if (ImGui.InputText("Spotify Client ID", ref _spotifyClientIdBuffer, SPOTIFY_CLIENT_ID_MAX_LENGTH))
        {
            Config.SpotifyClientId = _spotifyClientIdBuffer;
            Config.Save();
        }

        if (ImGui.InputText("Spotify Client Secret", ref _spotifyClientSecretBuffer, SPOTIFY_CLIENT_SECRET_MAX_LENGTH, ImGuiInputTextFlags.Password))
        {
            Config.SpotifyClientSecret = _spotifyClientSecretBuffer;
            Config.Save();
        }

        if (ImGui.Button("Authenticate with Spotify"))
        {
            if (!Config.SpotifyClientId.IsNullOrWhitespace() && !Config.SpotifyClientSecret.IsNullOrWhitespace())
            {
                _ = StartSpotifyAuth();
            }
        }

        ImGui.SameLine();
        if (Config.SpotifyRefreshToken.IsNullOrWhitespace())
        {
            ImGui.TextColored(ImGuiColors.DalamudRed, "State: Not Authenticated");
        }
        else
        {
            if (_cachedAuthTime != Config.LastSpotifyAuthTime)
            {
                _lastAuthTimeString = $"State: Authenticated (Last refresh: {Config.LastSpotifyAuthTime})";
                _cachedAuthTime = Config.LastSpotifyAuthTime;
            }
            ImGui.TextColored(ImGuiColors.ParsedGreen, _lastAuthTimeString);
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(
                "Instructions:\n" +
                "1. Go to the Spotify Developer Dashboard (developer.spotify.com/dashboard).\n" +
                "2. Create a new App.\n" +
                "3. Note the Client ID and Client Secret and paste them here.\n" +
                "4. In your Spotify App settings, click 'Edit Settings'.\n" +
                "5. Add 'http://127.0.0.1:5000/callback' to your Redirect URIs.\n" +
                "6. Click the 'Add' button, then click the 'Save' button at the bottom.\n" +
                "7. Click the 'Authenticate with Spotify' button (here) to log in."
            );
        }
    }

    private void DrawActivityConfigTabs()
    {
        var recreateWidth = ImGui.CalcTextSize(RecreateText).X + (ImGui.GetStyle().FramePadding.X * 2.0f);
        var deleteWidth = ImGui.CalcTextSize("Delete All").X + (ImGui.GetStyle().FramePadding.X * 2.0f);
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var windowPadding = ImGui.GetStyle().WindowPadding.X * 2.0f;

        var windowWidth = ImGui.GetWindowWidth();
        var rightButtonsWidth = recreateWidth + spacing + deleteWidth;
        var recreatePos = windowWidth - windowPadding - rightButtonsWidth;

        if (ImGui.Button("New##activityConfigsNew"))
        {
            var newConfig = new ActivityConfig
            {
                Name = $"New Config {Config.ActivityConfigs.Count + 1}"
            };
            Config.ActivityConfigs.Add(newConfig);

            if (string.IsNullOrEmpty(Config.ActiveConfigName))
            {
                Config.ActiveConfigName = newConfig.Name;
            }

            _newlyCreatedTabName = newConfig.Name;

            Config.Save();
        }

        ImGui.SameLine(recreatePos);

        if (ImGui.Button(RecreateText + "##activityConfigsRecreateDefaults"))
        {
            var defaults = ActivityConfig.GetDefaults();
            Config.ActivityConfigs.AddRange(defaults);

            if (string.IsNullOrEmpty(Config.ActiveConfigName) && defaults.Count > 0)
            {
                Config.ActiveConfigName = defaults[0].Name;
            }

            if (defaults.Count > 0)
            {
                _newlyCreatedTabName = defaults[0].Name;
            }

            Config.Save();
        }

        ImGui.SameLine();
        ImGui.PushStyleColor(ImGuiCol.Button, ImGuiColors.DalamudRed);
        if (ImGui.Button("Delete All##activityConfigsDeleteAll"))
        {
            Config.ActivityConfigs.Clear();
            Config.ActiveConfigName = string.Empty;
            Config.Save();
        }
        ImGui.PopStyleColor();

        if (ImGui.BeginTabBar("activityConfigsTabBar"))
        {
            for (var i = Config.ActivityConfigs.Count - 1; i >= 0; i--)
            {
                DrawSingleActivityTab(Config.ActivityConfigs[i]);
            }
            ImGui.EndTabBar();
        }
    }

    private void DrawSingleActivityTab(ActivityConfig activityConfig)
    {
        var activityConfigId = $"activityConfigs{activityConfig.GetHashCode()}";
        var name = activityConfig.Name;
        var tabTitle = $"{(name.IsNullOrWhitespace() ? "(Blank)" : name)}###{activityConfigId}TabItem";

        var flags = ImGuiTabItemFlags.None;
        if (_newlyCreatedTabName != null && _newlyCreatedTabName == activityConfig.Name)
        {
            flags = ImGuiTabItemFlags.SetSelected;
            _newlyCreatedTabName = null;
        }

        if (!ImGui.BeginTabItem(tabTitle, flags)) return;

        ImGui.Indent(10);

        if (ImGui.InputText($"Name###{activityConfigId}Name", ref name, MAX_INPUT_LENGTH))
        {
            activityConfig.Name = name;
            Config.Save();
        }

        var exportWidth = ImGui.CalcTextSize("Export").X + (ImGui.GetStyle().FramePadding.X * 2.0f);
        var importWidth = ImGui.CalcTextSize("Import").X + (ImGui.GetStyle().FramePadding.X * 2.0f);
        var deleteWidth = ImGui.CalcTextSize("Delete").X + (ImGui.GetStyle().FramePadding.X * 2.0f);
        var buttonSpacing = ImGui.GetStyle().ItemSpacing.X;
        var totalWidth = exportWidth + importWidth + deleteWidth + (buttonSpacing * 2);

        ImGui.SameLine(ImGui.GetContentRegionAvail().X + ImGui.GetCursorPosX() - totalWidth);

        if (ImGui.Button($"Export###{activityConfigId}Export"))
        {
            var json = activityConfig.ExportToJson();
            ImGui.SetClipboardText(json);
            Plugin.ChatGui.Print("✓ Config exported to clipboard!");
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Copy this config as JSON to clipboard for sharing");
        }

        ImGui.SameLine();
        if (ImGui.Button($"Import###{activityConfigId}Import"))
        {
            var clipboardText = ImGui.GetClipboardText();
            if (ActivityConfig.TryImportFromJson(clipboardText, out var importedConfig, out var error))
            {
                importedConfig!.Name = $"{importedConfig.Name} (Imported)";
                activityConfig.Name = importedConfig.Name;
                activityConfig.TypeName = importedConfig.TypeName;
                activityConfig.FilterTemplate = importedConfig.FilterTemplate;
                activityConfig.TitleTemplate = importedConfig.TitleTemplate;
                activityConfig.IsPrefix = importedConfig.IsPrefix;
                activityConfig.RainbowMode = importedConfig.RainbowMode;
                activityConfig.Color = importedConfig.Color;
                activityConfig.Glow = importedConfig.Glow;
                Config.Save();
                Plugin.ChatGui.Print("✓ Config imported from clipboard!");
            }
            else
            {
                Plugin.ChatGui.PrintError($"✗ Import failed: {error}");
            }
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Import config from clipboard (paste JSON)");
        }

        ImGui.SameLine();
        ImGui.PushStyleColor(ImGuiCol.Button, ImGuiColors.DalamudRed);
        if (ImGui.Button($"Delete###{activityConfigId}Delete"))
        {
            Config.ActivityConfigs.Remove(activityConfig);
            if (Config.ActiveConfigName == activityConfig.Name && Config.ActivityConfigs.Count > 0)
            {
                Config.ActiveConfigName = Config.ActivityConfigs[0].Name;
            }
            Config.Save();
        }
        ImGui.PopStyleColor();

        DrawTemplateVariablesTable(activityConfigId);

        var filterTemplate = activityConfig.FilterTemplate;
        var titleTemplate = activityConfig.TitleTemplate;
        var availableWidth = ImGui.GetContentRegionAvail().X;

        if (DrawTemplateInput($"Filter Template (scriban)###{activityConfigId}FilterTemplate",
                             ref filterTemplate,
                             new(availableWidth, 50),
                             "Expects parsable boolean as output if provided\nSyntax reference available on https://github.com/scriban/scriban"))
        {
            activityConfig.FilterTemplate = filterTemplate;
            Config.Save();
        }

        if (DrawTemplateInput($"Title Template (scriban)###{activityConfigId}TitleTemplate",
                             ref titleTemplate,
                             new(availableWidth, 450),
                             "Expects single line as output (max: 32 characters)\nSyntax reference available on https://github.com/scriban/scriban"))
        {
            activityConfig.TitleTemplate = titleTemplate;
            Config.Save();
        }

        DrawTitleStyleSettings(activityConfig, activityConfigId);
        ImGui.Spacing();
        DrawTemplatePreview(activityConfig);

        ImGui.Unindent();
        ImGui.EndTabItem();
    }

    private static void DrawTemplatePreview(ActivityConfig activityConfig)
    {
        ImGui.Separator();
        ImGui.Text("Live Preview:");
        ImGui.Spacing();
        ImGui.Indent(10);

        try
        {
            var mockTrack = CreateMockSpotifyTrack();
            var mockContext = new UpdaterContext { SecsElapsed = 0 };

            var titleTemplate = Template.Parse(activityConfig.TitleTemplate);
            if (titleTemplate.HasErrors)
            {
                var errorMessages = new List<string>(titleTemplate.Messages.Count);
                foreach (var msg in titleTemplate.Messages)
                {
                    errorMessages.Add(msg.Message);
                }
                ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.DalamudRed);
                ImGui.TextWrapped($"Template Error: {string.Join(", ", errorMessages)}");
                ImGui.PopStyleColor();
            }
            else
            {
                var renderedTitle = titleTemplate.Render(new { Activity = mockTrack, Context = mockContext }, member => member.Name);

                ImGui.Text("Result:");
                ImGui.SameLine();

                var colorToUse = activityConfig.Color ?? new Vector3(1, 1, 1);
                if (activityConfig.RainbowMode)
                {
                    ImGui.TextColored(new Vector4(1, 0.5f, 0, 1), "🌈 ");
                    ImGui.SameLine();
                }

                ImGui.TextColored(new Vector4(colorToUse.X, colorToUse.Y, colorToUse.Z, 1), renderedTitle);

                var length = renderedTitle.Length;
                var lengthColor = length <= 32 ? ImGuiColors.HealerGreen : ImGuiColors.DalamudRed;
                ImGui.SameLine();
                ImGui.TextColored(lengthColor, $"({length}/32)");

                if (length > 32)
                {
                    ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.DalamudOrange);
                    ImGui.TextWrapped("⚠ Title exceeds 32 character limit and will be rejected by Honorific plugin.");
                    ImGui.PopStyleColor();
                }
            }
        }
        catch (Exception ex)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.DalamudRed);
            ImGui.TextWrapped($"Preview Error: {ex.Message}");
            ImGui.PopStyleColor();
        }

        ImGui.Unindent();
    }

    private static object CreateMockSpotifyTrack()
    {
        return new
        {
            Name = "Never Gonna Give You Up",
            Artists = new[]
            {
                new { Name = "Rick Astley" }
            },
            Album = new { Name = "Whenever You Need Somebody" },
            DurationMs = 213000,
            Popularity = 85
        };
    }

    private static void DrawTemplateVariablesTable(string activityConfigId)
    {
        if (!ImGui.CollapsingHeader($"Available Template Variables###{activityConfigId}Properties")) return;

        if (ImGui.BeginTable($"{activityConfigId}Properties", 2, ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.Resizable, new(ImGui.GetWindowWidth(), 200)))
        {
            ImGui.TableSetupColumn($"Name###{activityConfigId}PropertyNames", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn($"Type###{activityConfigId}PropertyTypes", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableHeadersRow();

            ImGui.TableNextRow(); ImGui.TableNextColumn(); ImGui.Text("Activity.Name"); ImGui.TableNextColumn(); ImGui.Text("System.String");
            ImGui.TableNextRow(); ImGui.TableNextColumn(); ImGui.Text("Activity.Artists"); ImGui.TableNextColumn(); ImGui.Text("System.Collections.Generic.List<SimpleArtist>");
            ImGui.TableNextRow(); ImGui.TableNextColumn(); ImGui.Text("Activity.Artists[0].Name"); ImGui.TableNextColumn(); ImGui.Text("System.String");
            ImGui.TableNextRow(); ImGui.TableNextColumn(); ImGui.Text("Activity.Album.Name"); ImGui.TableNextColumn(); ImGui.Text("System.String");
            ImGui.TableNextRow(); ImGui.TableNextColumn(); ImGui.Text("Activity.DurationMs"); ImGui.TableNextColumn(); ImGui.Text("System.Int32");
            ImGui.TableNextRow(); ImGui.TableNextColumn(); ImGui.Text("Activity.Popularity"); ImGui.TableNextColumn(); ImGui.Text("System.Int32");

            foreach (var property in UpdaterContextProperties)
            {
                if (ImGui.TableNextColumn())
                {
                    ImGui.Text($"Context.{property.Name}");
                }
                if (ImGui.TableNextColumn())
                {
                    ImGui.Text(property.PropertyType.ScriptPrettyName());
                }
            }

            ImGui.EndTable();
        }
    }

    private static bool DrawTemplateInput(string label, ref string template, Vector2 size, string validTooltip)
    {
        var changed = ImGui.InputTextMultiline(label, ref template, MAX_INPUT_LENGTH, size);

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(validTooltip);
        }
        return changed;
    }

    private void DrawTitleStyleSettings(ActivityConfig activityConfig, string activityConfigId)
    {
        var isPrefix = activityConfig.IsPrefix;
        if (ImGui.Checkbox($"Prefix###{activityConfigId}Prefix", ref isPrefix))
        {
            activityConfig.IsPrefix = isPrefix;
            Config.Save();
        }

        ImGui.SameLine();
        var rainbowMode = activityConfig.RainbowMode;
        if (ImGui.Checkbox($"Rainbow Mode###{activityConfigId}Rainbow", ref rainbowMode))
        {
            activityConfig.RainbowMode = rainbowMode;
            Config.Save();
        }

        ImGui.SameLine();

        var checkboxSize = new Vector2(ImGui.GetTextLineHeightWithSpacing(), ImGui.GetTextLineHeightWithSpacing());

        ImGui.BeginDisabled(activityConfig.RainbowMode);
        var color = activityConfig.Color;
        if (ImGuiHelper.DrawColorPicker($"Color###{activityConfigId}Color", ref color, checkboxSize))
        {
            activityConfig.Color = color;
            Config.Save();
        }
        ImGui.EndDisabled();

        ImGui.SameLine();
        var glow = activityConfig.Glow;
        if (ImGuiHelper.DrawColorPicker($"Glow###{activityConfigId}Glow", ref glow, checkboxSize))
        {
            activityConfig.Glow = glow;
            Config.Save();
        }
    }

    private async Task StartSpotifyAuth()
    {
        try
        {
            var serverUri = new Uri("http://127.0.0.1:5000");
            SpotifyAuthServer?.Dispose();
            SpotifyAuthServer = new PKCECallbackActivator(serverUri, "callback");

            await SpotifyAuthServer.Start().ConfigureAwait(false);

            var (verifier, challenge) = PKCEUtil.GenerateCodes();
            var loginRequest = new LoginRequest(SpotifyAuthServer.RedirectUri, Config.SpotifyClientId, LoginRequest.ResponseType.Code)
            {
                CodeChallenge = challenge,
                CodeChallengeMethod = "S256",
                Scope = [Scopes.UserReadCurrentlyPlaying, Scopes.UserReadPlaybackState]
            };

            BrowserUtil.Open(loginRequest.ToUri());

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(SPOTIFY_AUTH_TIMEOUT_MINUTES));
            var context = await SpotifyAuthServer.ReceiveContext(timeoutCts.Token).ConfigureAwait(false);

            var code = context.Request.QueryString["code"];
            if (string.IsNullOrEmpty(code))
            {
                Plugin.PluginLog.Error("Spotify auth failed: No code received.");
                return;
            }

            var tokenResponse = await new OAuthClient().RequestToken(
                new PKCETokenRequest(Config.SpotifyClientId, code, SpotifyAuthServer.RedirectUri, verifier)
            ).ConfigureAwait(false);

            Config.SpotifyRefreshToken = tokenResponse.RefreshToken;
            Config.LastSpotifyAuthTime = DateTime.Now;
            Config.Save();

            Plugin.PluginLog.Information("Successfully authenticated with Spotify!");
        }
        catch (Exception e)
        {
            Plugin.PluginLog.Error(e, "Spotify authentication failed");
        }
        finally
        {
            SpotifyAuthServer?.Dispose();
            SpotifyAuthServer = null;
        }
    }

    public override void OnClose()
    {
        SpotifyAuthServer?.Dispose();
        SpotifyAuthServer = null;
    }
}
