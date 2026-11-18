namespace GaugeOverlay.App.UI;

/// <summary>설정's sizes, one place for every view: the values and what each is for are in
/// <c>.interface-design/system.md</c> (approved 2026-11-16, <c>ui-motion-tokens.plan.md</c> phase 2). Not the in-game overlay.</summary>
internal static class Tokens
{
    /// <summary>Margins and paddings, on a 4 px base.</summary>
    internal static class Space
    {
        /// <summary>Text to its mark, check box rows.</summary>
        public const double Hair = 2;
        /// <summary>A row's top and bottom, icon to text.</summary>
        public const double Tight = 4;
        /// <summary>Control to control, a heading to its first row.</summary>
        public const double Gap = 8;
        /// <summary>Above a sub-heading, below a note.</summary>
        public const double Block = 12;
        /// <summary>A card's padding, check box to check box.</summary>
        public const double Card = 16;
        /// <summary>Card to card, above a heading.</summary>
        public const double Section = 24;
        /// <summary>The page's edge.</summary>
        public const double Page = 32;
    }

    /// <summary>Font sizes. Nothing under <see cref="Hint"/>.</summary>
    internal static class Font
    {
        public const double Title = 20, Section = 16, Body = 14, Hint = 12;
    }

    internal static class Radius
    {
        /// <summary>Swatches, map marks, bars.</summary>
        public const double Mark = 2;
        /// <summary>Buttons, boxes, combos (Fluent's own).</summary>
        public const double Control = 4;
        /// <summary>Cards, dialogs, popups.</summary>
        public const double Card = 8;
    }

    /// <summary>The setting row and the page around it.</summary>
    internal static class Row
    {
        public const double Height = 32, LabelColumn = 160;
        /// <summary>The three control widths: numbers and keys, combos and sliders with their value, names and codes.</summary>
        public const double Short = 96, Medium = 200, Long = 320;
        /// <summary>The content column's widest (Core's, which also sizes the form beside a preview).</summary>
        public const double ContentWidth = Core.Domain.SettingsPreview.FormWidest;
    }
}
