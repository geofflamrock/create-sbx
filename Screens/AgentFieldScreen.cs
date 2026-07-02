using CreateSbx.Models;
using CreateSbx.Widgets;

namespace CreateSbx.Screens;

/// <summary>Agent field editor: pick a built-in agent, or "Custom agent..." to type an identifier.</summary>
internal sealed class AgentFieldScreen : Screen
{
    private const string CustomSentinel = "__custom__";

    private IStep _current;

    public AgentFieldScreen(string currentAgentId, Action<ApplicationContext, string> onConfirm)
    {
        var options = SandboxConfig.BuiltInAgents.Select(a => a.Id).Append(CustomSentinel).ToList();
        var selectedIndex = Math.Max(0, options.IndexOf(currentAgentId));

        _current = new SingleSelectEditorScreen<string>(
            "Select agent",
            options,
            FormatAgent,
            (context, chosen) =>
            {
                if (chosen == CustomSentinel)
                {
                    _current = new TextFieldEditorScreen(
                        "Enter the custom agent identifier",
                        currentAgentId,
                        onConfirm);
                    return;
                }

                onConfirm(context, chosen);
            },
            selectedIndex);
    }

    private static string FormatAgent(string id)
    {
        if (id == CustomSentinel)
        {
            return "[grey]Custom agent…[/]";
        }

        var option = SandboxConfig.BuiltInAgents.First(a => a.Id == id);
        return option.Description is not null
            ? $"{MarkupText.Escape(option.DisplayName)} [grey]({option.Id})[/] [grey]- {MarkupText.Escape(option.Description)}[/]"
            : $"{MarkupText.Escape(option.DisplayName)} [grey]({option.Id})[/]";
    }

    public override void OnMessage(ApplicationContext context, ApplicationMessage message) =>
        _current.OnMessage(context, message);

    public override void Render(RenderContext context) => _current.Render(context);
}
