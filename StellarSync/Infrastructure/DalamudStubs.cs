using System;
using System.Numerics;

namespace StellarSync.Infrastructure
{
    // Dalamud Plugin Interface
    public interface IDalamudPlugin
    {
        string Name { get; }
        void Dispose();
    }

    public interface IPluginConfiguration
    {
        int Version { get; set; }
    }

    // Service interfaces
    public interface ICommandManager
    {
        void AddHandler(string commandName, CommandInfo commandInfo);
        void RemoveHandler(string commandName);
    }

    public interface IClientState
    {
        bool IsLoggedIn { get; }
        bool IsPvP { get; }
        string PlayerName { get; }
        ulong LocalPlayerId { get; }
    }

    public interface IObjectTable
    {
        // Stub interface for object table
    }

    public interface IGameGui
    {
        // Stub interface for game GUI
    }

    public class DalamudPluginInterface
    {
        public UiBuilder UiBuilder { get; } = new UiBuilder();
        public CommandManager CommandManager { get; } = new CommandManager();

        public T? GetPluginConfig<T>() where T : class, IPluginConfiguration, new()
        {
            return new T();
        }

        public void SavePluginConfig<T>(T config) where T : class, IPluginConfiguration
        {
            // Stub implementation
        }
    }

    public class UiBuilder
    {
        public event Action? Draw;
        public event Action? OpenConfigUi;
    }

    public class CommandManager : ICommandManager
    {
        public void AddHandler(string commandName, CommandInfo commandInfo)
        {
            // Stub implementation
        }

        public void RemoveHandler(string commandName)
        {
            // Stub implementation
        }
    }

    public class CommandInfo
    {
        public string HelpMessage { get; set; } = "";
        public Action<string, string> Handler { get; }

        public CommandInfo(Action<string, string> handler)
        {
            Handler = handler;
        }
    }

    // Attributes
    [AttributeUsage(AttributeTargets.Class)]
    public class RequiredVersionAttribute : Attribute
    {
        public RequiredVersionAttribute(int version) { }
    }

    [AttributeUsage(AttributeTargets.Property)]
    public class PluginServiceAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Class)]
    public class ApiVersionAttribute : Attribute
    {
        public ApiVersionAttribute(int version) { }
    }

    // ImGui stubs
    public static class ImGui
    {
        public static bool Begin(string name, ref bool open, ImGuiWindowFlags flags = ImGuiWindowFlags.None)
        {
            return true;
        }

        public static void End()
        {
        }

        public static void SetNextWindowSize(Vector2 size, ImGuiCond cond = ImGuiCond.None)
        {
        }

        public static bool InputText(string label, ref string text, uint maxLength = 256, ImGuiInputTextFlags flags = ImGuiInputTextFlags.None)
        {
            return false;
        }

        public static bool Checkbox(string label, ref bool v)
        {
            return false;
        }

        public static bool Button(string label, Vector2? size = null)
        {
            return false;
        }

        public static void Text(string text)
        {
        }

        public static void SameLine(float offsetFromStartX = 0.0f, float spacing = -1.0f)
        {
        }

        public static void Separator()
        {
        }

        public static bool TreeNode(string label, ImGuiTreeNodeFlags flags = ImGuiTreeNodeFlags.None)
        {
            return false;
        }

        public static void TreePop()
        {
        }

        public static void PushStyleColor(ImGuiCol idx, Vector4 col)
        {
        }

        public static void PopStyleColor(int count = 1)
        {
        }

        public static void CloseCurrentPopup()
        {
        }

        public static bool BeginPopupModal(string name, ref bool open, ImGuiWindowFlags flags = ImGuiWindowFlags.None)
        {
            return false;
        }

        public static void EndPopup()
        {
        }
    }

    public enum ImGuiWindowFlags
    {
        None = 0,
        AlwaysAutoResize = 1 << 4
    }

    public enum ImGuiInputTextFlags
    {
        None = 0,
        Password = 1 << 1
    }

    public enum ImGuiTreeNodeFlags
    {
        None = 0,
        DefaultOpen = 1 << 0
    }

    public enum ImGuiCol
    {
        Text = 0,
        TextDisabled = 1,
        WindowBg = 2,
        ChildBg = 3,
        PopupBg = 4,
        Border = 5,
        BorderShadow = 6,
        FrameBg = 7,
        FrameBgHovered = 8,
        FrameBgActive = 9,
        TitleBg = 10,
        TitleBgActive = 11,
        TitleBgCollapsed = 12,
        MenuBarBg = 13,
        ScrollbarBg = 14,
        ScrollbarGrab = 15,
        ScrollbarGrabHovered = 16,
        ScrollbarGrabActive = 17,
        CheckMark = 18,
        SliderGrab = 19,
        SliderGrabActive = 20,
        Button = 21,
        ButtonHovered = 22,
        ButtonActive = 23,
        Header = 24,
        HeaderHovered = 25,
        HeaderActive = 26,
        Separator = 27,
        SeparatorHovered = 28,
        SeparatorActive = 29,
        ResizeGrip = 30,
        ResizeGripHovered = 31,
        ResizeGripActive = 32,
        Tab = 33,
        TabHovered = 34,
        TabActive = 35,
        TabUnfocused = 36,
        TabUnfocusedActive = 37,
        PlotLines = 38,
        PlotLinesHovered = 39,
        PlotHistogram = 40,
        PlotHistogramHovered = 41,
        TableHeaderBg = 42,
        TableBorderStrong = 43,
        TableBorderLight = 44,
        TableRowBg = 45,
        TableRowBgAlt = 46,
        TextSelectedBg = 47,
        DragDropTarget = 48,
        NavHighlight = 49,
        NavWindowingHighlight = 50,
        NavWindowingDimBg = 51,
        ModalWindowDimBg = 52
    }

    public enum ImGuiCond
    {
        None = 0,
        Always = 1 << 0,
        Once = 1 << 1,
        FirstUseEver = 1 << 2,
        Appearing = 1 << 3
    }
}
