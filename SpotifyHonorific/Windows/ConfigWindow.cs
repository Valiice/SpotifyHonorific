using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Utility;
using SpotifyHonorific.Activities;
using SpotifyHonorific.Updaters;
using SpotifyHonorific.Utils;
using Dalamud.Bindings.ImGui;
using Scriban;
using System.Linq;
using System.Numerics;
using Scriban.Helpers;
using SpotifyAPI.Web.Auth;
using SpotifyAPI.Web;
using System;
using System.Threading.Tasks;
using System.Threading;

namespace SpotifyHonorific.Windows;

public class ConfigWindow : Window
{
    private Config Config { get; init; }
    private ImGuiHelper ImGuiHelper { get; init; }
    private Updater Updater { get; init; }

    private string _spotifyClientIdBuffer = string.Empty;
    private string _spotifyClientSecretBuffer = string.Empty;
    private static PKCECallbackActivator? _spotifyAuthServer;

    private string _lastAuthTimeString = string.Empty;
    private DateTime _cachedAuthTime;

    private static readonly string RecreateText = $"Recreate Defaults (V{ActivityConfig.DEFAULT_VERSION})";

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
        ImGui.Spacing();
        DrawActivityConfigTabs();
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
    }

    private void DrawSpotifySetup()
    {
        ImGui.Text("Spotify Setup");

        if (ImGui.InputText("Spotify Client ID", ref _spotifyClientIdBuffer, 100))
        {
            Config.SpotifyClientId = _spotifyClientIdBuffer;
            Config.Save();
        }

        if (ImGui.InputText("Spotify Client Secret", ref _spotifyClientSecretBuffer, 100, ImGuiInputTextFlags.Password))
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
        if (ImGui.Button("New##activityConfigsNew"))
        {
            Config.ActivityConfigs.Add(new());
            Config.Save();
        }

        var recreateWidth = ImGui.CalcTextSize(RecreateText).X + (ImGui.GetStyle().FramePadding.X * 2.0f);
        var deleteWidth = ImGui.CalcTextSize("Delete All").X + (ImGui.GetStyle().FramePadding.X * 2.0f);
        var spacing = ImGui.GetStyle().ItemSpacing.X;

        var contentWidth = ImGui.GetContentRegionMax().X;

        ImGui.SameLine(contentWidth - (recreateWidth + deleteWidth + spacing));

        if (ImGui.Button(RecreateText + "##activityConfigsRecreateDefaults"))
        {
            Config.ActivityConfigs.AddRange(ActivityConfig.GetDefaults());
            Config.Save();
        }

        ImGui.SameLine();
        ImGui.PushStyleColor(ImGuiCol.Button, ImGuiColors.DalamudRed);
        if (ImGui.Button("Delete All##activityConfigsDeleteAll"))
        {
            Config.ActivityConfigs.Clear();
            Config.Save();
        }
        ImGui.PopStyleColor();

        if (ImGui.BeginTabBar("activityConfigsTabBar"))
        {
            foreach (var activityConfig in Config.ActivityConfigs.ToList())
            {
                DrawSingleActivityTab(activityConfig);
            }
            ImGui.EndTabBar();
        }
    }

    private void DrawSingleActivityTab(ActivityConfig activityConfig)
    {
        var activityConfigId = $"activityConfigs{activityConfig.GetHashCode()}";
        var name = activityConfig.Name;
        var tabTitle = $"{(name.IsNullOrWhitespace() ? "(Blank)" : name)}###{activityConfigId}TabItem";

        if (!ImGui.BeginTabItem(tabTitle)) return;

        ImGui.Indent(10);

        var activityConfigEnabled = activityConfig.Enabled;
        if (ImGui.Checkbox($"Enabled###{activityConfigId}enabled", ref activityConfigEnabled))
        {
            activityConfig.Enabled = activityConfigEnabled;
            Config.Save();
        }

        ImGui.SameLine(ImGui.GetContentRegionAvail().X + ImGui.GetCursorPosX() - (ImGui.CalcTextSize("Delete").X + (ImGui.GetStyle().FramePadding.X * 2.0f)));
        ImGui.PushStyleColor(ImGuiCol.Button, ImGuiColors.DalamudRed);
        if (ImGui.Button($"Delete###{activityConfigId}Delete"))
        {
            Config.ActivityConfigs.Remove(activityConfig);
            Config.Save();
        }
        ImGui.PopStyleColor();

        if (ImGui.InputText($"Name###{activityConfigId}Name", ref name, ushort.MaxValue))
        {
            activityConfig.Name = name;
            Config.Save();
        }

        var priority = activityConfig.Priority;
        if (ImGui.InputInt($"Priority###{activityConfigId}Priority", ref priority, 1))
        {
            activityConfig.Priority = priority;
            Config.Save();
        }

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

        var availableHeight = ImGui.GetContentRegionAvail().Y;
        if (DrawTemplateInput($"Title Template (scriban)###{activityConfigId}TitleTemplate",
                             ref titleTemplate,
                             new(availableWidth, availableHeight - 40),
                             "Expects single line as output (max: 32 characters)\nSyntax reference available on https://github.com/scriban/scriban"))
        {
            activityConfig.TitleTemplate = titleTemplate;
            Config.Save();
        }

        DrawTitleStyleSettings(activityConfig, activityConfigId);

        ImGui.Unindent();
        ImGui.EndTabItem();
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

            foreach (var property in typeof(UpdaterContext).GetProperties())
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
        var changed = ImGui.InputTextMultiline(label, ref template, ushort.MaxValue, size);

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
        ImGui.Spacing();
        ImGui.SameLine();

        var rainbowMode = activityConfig.RainbowMode;
        if (ImGui.Checkbox($"Rainbow Mode###{activityConfigId}Rainbow", ref rainbowMode))
        {
            activityConfig.RainbowMode = rainbowMode;
            Config.Save();
        }

        ImGui.SameLine();
        ImGui.Spacing();
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
        ImGui.Spacing();
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
            _spotifyAuthServer?.Dispose();
            _spotifyAuthServer = new PKCECallbackActivator(serverUri, "callback");

            await _spotifyAuthServer.Start().ConfigureAwait(false);

            var (verifier, challenge) = PKCEUtil.GenerateCodes();
            var loginRequest = new LoginRequest(_spotifyAuthServer.RedirectUri, Config.SpotifyClientId, LoginRequest.ResponseType.Code)
            {
                CodeChallenge = challenge,
                CodeChallengeMethod = "S256",
                Scope = new[] { Scopes.UserReadCurrentlyPlaying, Scopes.UserReadPlaybackState }
            };

            BrowserUtil.Open(loginRequest.ToUri());

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(1));
            var context = await _spotifyAuthServer.ReceiveContext(timeoutCts.Token).ConfigureAwait(false);

            var code = context.Request.QueryString["code"];
            if (string.IsNullOrEmpty(code))
            {
                Plugin.PluginLog.Error("Spotify auth failed: No code received.");
                return;
            }

            var tokenResponse = await new OAuthClient().RequestToken(
                new PKCETokenRequest(Config.SpotifyClientId, code, _spotifyAuthServer.RedirectUri, verifier)
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
            _spotifyAuthServer?.Dispose();
            _spotifyAuthServer = null;
        }
    }

    public override void OnClose()
    {
        _spotifyAuthServer?.Dispose();
        _spotifyAuthServer = null;
    }

    private static bool TryParseTemplate(string template, out LogMessageBag errorMessages)
    {
        var parsed = Template.Parse(template);
        errorMessages = parsed.Messages;
        return !parsed.HasErrors;
    }
}
