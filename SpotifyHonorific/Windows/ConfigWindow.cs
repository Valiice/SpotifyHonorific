using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Components;
using Dalamud.Interface.Windowing;
using Dalamud.Utility;
using SpotifyHonorific.Activities;
using SpotifyHonorific.Authentication;
using SpotifyHonorific.Core;
using SpotifyHonorific.Gradient;
using SpotifyHonorific.Updaters;
using SpotifyHonorific.Utils;
using Dalamud.Bindings.ImGui;
using System.Numerics;
using Scriban;
using Scriban.Helpers;
using System;
using System.Threading.Tasks;

namespace SpotifyHonorific.Windows;

public class ConfigWindow : Window
{
    private const int SPOTIFY_CLIENT_ID_MAX_LENGTH = 100;
    private const int SPOTIFY_CLIENT_SECRET_MAX_LENGTH = 100;
    private const ushort MAX_INPUT_LENGTH = ushort.MaxValue;

    private Config Config { get; init; }
    private ImGuiHelper ImGuiHelper { get; init; }
    private Updater Updater { get; init; }
    private SpotifyAuthenticator SpotifyAuthenticator { get; init; }
    private PlaybackState PlaybackState { get; init; }

    private string _spotifyClientIdBuffer = string.Empty;
    private string _spotifyClientSecretBuffer = string.Empty;

    private string _lastAuthTimeString = string.Empty;
    private DateTime _cachedAuthTime;
    private string? _newlyCreatedTabName;
    private string[] _cachedConfigNames = [];
    private int _cachedConfigCount;
    private float _kofiButtonWidth;
    private bool _confirmDeleteAll;

    private static readonly string RecreateText = "Recreate Defaults";
    private static readonly System.Reflection.PropertyInfo[] UpdaterContextProperties = typeof(UpdaterContext).GetProperties();

    public ConfigWindow(Config config, ImGuiHelper imGuiHelper, Updater updater, SpotifyAuthenticator spotifyAuthenticator, PlaybackState playbackState) : base("Spotify Activity Honorific Config##configWindow")
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new(760, 420),
            MaximumSize = new(float.MaxValue, float.MaxValue)
        };

        Config = config;
        ImGuiHelper = imGuiHelper;
        Updater = updater;
        SpotifyAuthenticator = spotifyAuthenticator;
        PlaybackState = playbackState;

        _spotifyClientIdBuffer = Config.SpotifyClientId;
        _spotifyClientSecretBuffer = Config.SpotifyClientSecret;
    }

    public override void Draw()
    {
        DrawKofiButton();
        DrawPersistentHeader();
        ImGui.Separator();
        DrawValidationErrors();
        ImGui.Spacing();

        if (ImGui.BeginTabBar("mainTabBar"))
        {
            if (ImGui.BeginTabItem("Config"))
            {
                ImGui.Spacing();
                DrawActiveConfigSelector();
                ImGui.Spacing();
                DrawActivityConfigTabs();
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Account"))
            {
                ImGui.Spacing();
                DrawAccountTab();
                ImGui.EndTabItem();
            }
            ImGui.EndTabBar();
        }
    }

    private void DrawKofiButton()
    {
        var startPos = ImGui.GetCursorPos();
        if (_kofiButtonWidth > 0)
        {
            ImGui.SetCursorPosX(ImGui.GetWindowContentRegionMax().X - _kofiButtonWidth);
        }
        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Coffee, "Support",
            new Vector4(1.0f, 0.35f, 0.35f, 0.9f),
            new Vector4(1.0f, 0.25f, 0.25f, 1.0f),
            new Vector4(1.0f, 0.35f, 0.35f, 0.75f)))
        {
            Dalamud.Utility.Util.OpenLink("https://ko-fi.com/valiice");
        }
        _kofiButtonWidth = ImGui.GetItemRectSize().X;
        ImGui.SetCursorPos(startPos);
    }

    private void DrawPersistentHeader()
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
            ImGui.SetTooltip("Stops polling Spotify when you are idle and no music is playing.\nPolling resumes at the normal rate when activity or music is detected.");
        }

    }

    private void DrawAccountTab()
    {
        DrawSpotifySetup();
        ImGui.Spacing();
        var enableNotifications = Config.EnableNotifications;
        if (ImGui.Checkbox("Notifications##notifications", ref enableNotifications))
        {
            Config.EnableNotifications = enableNotifications;
            Config.Save();
        }
        ImGui.SameLine();
        ImGui.TextDisabled("(?)");
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Shows a periodic warning notification when the plugin is enabled\nbut Spotify is not authenticated.");
        }

        ImGui.Spacing();
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
        var isSupporter = Config.IsHonorificSupporter;
        if (ImGui.Checkbox("Supporter##supporter", ref isSupporter))
        {
            Config.IsHonorificSupporter = isSupporter;
            Config.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Tick this if you support Honorific on Ko-fi.\nUnlocks gradient glow styles (supporter-only feature in Honorific).");
    }

    private void DrawValidationErrors()
    {
        if (Config.Validate(out var errors))
        {
            return;
        }

        ImGuiHelper.TextError("⚠ Configuration Issues:");

        ImGui.Indent(10);
        foreach (var error in errors)
        {
            ImGuiHelper.TextWarning($"• {error}");
        }
        ImGui.Unindent(10);
        ImGui.Separator();
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

        var currentIndex = ValidationHelper.FindActiveConfigIndex(Config.ActivityConfigs, Config.ActiveConfigName);

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
        if (_confirmDeleteAll)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, ImGuiColors.DalamudRed);
            if (ImGui.Button("Confirm delete all?##activityConfigsDeleteAllConfirm"))
            {
                Config.ActivityConfigs.Clear();
                Config.ActiveConfigName = string.Empty;
                Config.Save();
                _confirmDeleteAll = false;
            }
            ImGui.PopStyleColor();
            ImGui.SameLine();
            if (ImGui.Button("Cancel##activityConfigsDeleteAllCancel"))
            {
                _confirmDeleteAll = false;
            }
        }
        else
        {
            ImGui.PushStyleColor(ImGuiCol.Button, ImGuiColors.DalamudRed);
            if (ImGui.Button("Delete All##activityConfigsDeleteAll"))
            {
                _confirmDeleteAll = true;
            }
            ImGui.PopStyleColor();
        }

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
                activityConfig.GradientColourSet = importedConfig.GradientColourSet;
                activityConfig.GradientAnimationStyle = importedConfig.GradientAnimationStyle;
                activityConfig.Color3 = importedConfig.Color3;
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
        DrawTemplatePreview(activityConfig, PlaybackState);

        ImGui.Unindent();
        ImGui.EndTabItem();
    }

    private static void DrawTemplatePreview(ActivityConfig activityConfig, PlaybackState playbackState)
    {
        ImGui.Separator();
        ImGui.Text("Live Preview:");
        ImGui.Spacing();
        ImGui.Indent(10);

        var isMock = playbackState.CurrentTrack == null;
        var track = isMock ? (object)CreateMockSpotifyTrack() : playbackState.CurrentTrack!;
        var mockContext = new UpdaterContext { SecsElapsed = 0 };

        try
        {
            var titleTemplate = Template.Parse(activityConfig.TitleTemplate);
            if (titleTemplate.HasErrors)
            {
                ImGuiHelper.TextError($"Template Error: {TemplateHelper.GetTemplateErrors(titleTemplate)}");
            }
            else
            {
                var renderedTitle = titleTemplate.Render(new { Activity = track, Context = mockContext }, member => member.Name);

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

                if (isMock)
                {
                    ImGui.SameLine();
                    ImGui.TextDisabled("(mock)");
                }

                if (length > 32)
                {
                    ImGuiHelper.TextWarning("⚠ Title exceeds 32 character limit and will be rejected by Honorific plugin.");
                }
            }

            if (!string.IsNullOrWhiteSpace(activityConfig.FilterTemplate))
            {
                ImGui.Spacing();
                var filterResult = EvaluateFilterTemplate(activityConfig.FilterTemplate, track);
                if (filterResult == true)
                    ImGui.TextColored(ImGuiColors.HealerGreen, "✓ Filter matches");
                else
                    ImGui.TextColored(ImGuiColors.DalamudRed, "✗ Filter skipped");
            }
        }
        catch (Exception ex)
        {
            ImGuiHelper.TextError($"Preview Error: {ex.Message}");
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

    internal static bool? EvaluateFilterTemplate(string filterTemplate, object track)
    {
        if (string.IsNullOrWhiteSpace(filterTemplate))
            return null;

        try
        {
            var template = Template.Parse(filterTemplate);
            if (template.HasErrors)
                return false;

            var result = template.Render(new { Activity = track }, member => member.Name).Trim();
            return bool.TryParse(result, out var b) ? b : (bool?)false;
        }
        catch
        {
            return false;
        }
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

        // Preset gradients handle glow internally in Honorific; show picker only for no-gradient or two-color.
        if (activityConfig.GradientColourSet == null || activityConfig.GradientColourSet == -1)
        {
            ImGui.SameLine();
            var glow = activityConfig.Glow;
            var glowLabel = activityConfig.GradientColourSet == -1
                ? $"Color 1###{activityConfigId}GradColor1"
                : $"Glow###{activityConfigId}Glow";
            if (ImGuiHelper.DrawColorPicker(glowLabel, ref glow, checkboxSize))
            {
                activityConfig.Glow = glow;
                Config.Save();
            }
        }

        if (!Config.IsHonorificSupporter)
        {
            ImGui.SameLine();
            ImGui.TextDisabled("Gradient Glow (?)");
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Gradient glow styles are available for Ko-fi supporters.\nEnable the Supporter option in the Account tab to unlock.");
        }
        else
        {
            ImGui.Spacing();
            if (ImGui.CollapsingHeader($"Gradient Glow###{activityConfigId}CollapsingGradient"))
            {
                ImGui.Indent(10);
                DrawGradientSettings(activityConfig, activityConfigId, checkboxSize);
                ImGui.Unindent(10);
            }
        }
    }

    private void DrawGradientSettings(ActivityConfig activityConfig, string activityConfigId, Vector2 checkboxSize)
    {
        var currentLabel = activityConfig.GradientColourSet switch
        {
            null => "None",
            -1   => "Two Color",
            var i => GradientPresets.GetName(i!.Value)
        };

        ImGui.SetNextItemWidth(160);
        if (ImGui.BeginCombo($"###{activityConfigId}Gradient", currentLabel))
        {
            if (ImGui.Selectable("None", activityConfig.GradientColourSet == null))
            {
                activityConfig.GradientColourSet = null;
                activityConfig.GradientAnimationStyle = null;
                Config.Save();
            }

            if (ImGui.Selectable("Two Color", activityConfig.GradientColourSet == -1))
            {
                activityConfig.GradientColourSet = -1;
                activityConfig.GradientAnimationStyle ??= GradientAnimationStyle.Wave;
                activityConfig.Glow ??= new Vector3(0.81f, 0.35f, 0.82f);
                activityConfig.Color3 ??= new Vector3(1f, 0.84f, 0f);
                Config.Save();
            }

            ImGui.Separator();

            for (var i = 0; i < GradientPresets.NumPresets; i++)
            {
                if (ImGui.Selectable(GradientPresets.GetName(i), activityConfig.GradientColourSet == i))
                {
                    activityConfig.GradientColourSet = i;
                    activityConfig.GradientAnimationStyle ??= GradientAnimationStyle.Wave;
                    Config.Save();
                }
            }

            ImGui.EndCombo();
        }

        if (activityConfig.GradientColourSet == null) return;

        ImGui.SameLine();
        var animStyle = activityConfig.GradientAnimationStyle ?? GradientAnimationStyle.Wave;
        var animStyles = new[] { GradientAnimationStyle.Wave, GradientAnimationStyle.Pulse, GradientAnimationStyle.Static };
        var animNames = new[] { "Wave", "Pulse", "Static" };
        var animIndex = Array.IndexOf(animStyles, animStyle);
        if (animIndex < 0) animIndex = 0;
        ImGui.SetNextItemWidth(100);
        if (ImGui.Combo($"###{activityConfigId}AnimStyle", ref animIndex, animNames, animNames.Length))
        {
            activityConfig.GradientAnimationStyle = animStyles[animIndex];
            Config.Save();
        }

        if (activityConfig.GradientColourSet == -1)
        {
            var color3 = activityConfig.Color3;
            if (ImGuiHelper.DrawColorPicker($"Color 2###{activityConfigId}GradColor2", ref color3, checkboxSize))
            {
                activityConfig.Color3 = color3;
                Config.Save();
            }
        }
        else
        {
            ImGui.NewLine();
        }
    }

    private async Task StartSpotifyAuth()
    {
        await SpotifyAuthenticator.AuthenticateAsync().ConfigureAwait(false);
    }

    public override void OnClose()
    {
        SpotifyAuthenticator.Dispose();
    }
}
