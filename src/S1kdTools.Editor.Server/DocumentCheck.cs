using System.Xml;
using S1kdTools;

namespace S1kdTools.Editor.Server;

/// <summary>
/// What is wrong with the document as it stands, reported rather than prevented.
///
/// Three checks, each answering a question the author actually has, and in the
/// order the answers stop being useful — a module that is not well-formed cannot
/// be BREX-checked, and one that fails BREX can still be laid out:
///
/// * <b>Is it well-formed?</b> The parser's own message, with its line and column.
/// * <b>Does it follow the business rules?</b> <see cref="BrexCheck.CheckDefault"/>
///   against the S1000D default BREX for the module's own issue — no project BREX
///   is needed, and every finding carries the XPath of the element it is about, in
///   the same shape a block carries, so the editor can put the author in front of
///   it.
/// * <b>Can it be presented?</b> The FO transform is run and its exception, if any,
///   reported. An author who cannot see the page needs to know that it is the
///   module and not the preview. Skipped, with a warning, on a server started
///   without presentation stylesheets.
///
/// Nothing here refuses an edit. A data module halfway through being written is
/// invalid nearly all the time, and an editor that will not let an author leave a
/// paragraph until it validates is an editor they will leave instead.
/// </summary>
public sealed class DocumentCheck(EditorPresentation? presentation)
{
    /// <summary>Check <paramref name="xml"/> and report everything found.</summary>
    /// <param name="xml">The object, as the editor currently holds it.</param>
    /// <param name="schema">The schema it declares, for the presentation check.</param>
    /// <param name="title">The publication title, for the presentation check.</param>
    public CheckReport Check(string xml, string schema, string title)
    {
        var findings = new List<CheckFinding>();

        XmlDocument doc;
        try
        {
            doc = XmlUtils.ReadMem(xml);
        }
        catch (XmlException e)
        {
            // Nothing further can run, and nothing further would be believable.
            return new CheckReport(false, null,
                [new CheckFinding("error", "xml", e.Message, null, null)]);
        }

        string? brex = CheckBusinessRules(doc, findings);
        CheckPresentation(xml, schema, title, findings);

        return new CheckReport(
            !findings.Any(f => f.Severity == "error"),
            brex,
            [.. findings.OrderBy(f => f.Severity == "error" ? 0 : 1)]);
    }

    private static string? CheckBusinessRules(XmlDocument doc, List<CheckFinding> findings)
    {
        string code;
        XmlDocument report;
        try
        {
            code = BrexCheck.DefaultBrexDmc(doc);
            BrexCheck.CheckDefault(doc, BrexCheckOptions.Values | BrexCheckOptions.Notations, out report);
        }
        catch (Exception e)
        {
            // A schema this port has no default BREX for. Worth saying once, as a
            // warning: the module is not wrong, it is unchecked.
            findings.Add(new CheckFinding("warning", "brex",
                "The business rules could not be checked: " + e.Message, null, null));
            return null;
        }

        foreach (XmlElement error in report.SelectNodes("//brexCheck/document/brex/error")?
                     .OfType<XmlElement>() ?? [])
        {
            string severity = error.GetAttribute("fail") == "no" ? "warning" : "error";
            string use = Trim(error.SelectSingleNode("objectUse")?.InnerText);
            string rule = Trim(error.SelectSingleNode("objectPath")?.InnerText);

            // One finding per offending element, so each can be linked to its
            // block; a rule that nothing violated in particular still gets one.
            List<XmlElement> objects = [.. error.SelectNodes("object")?.OfType<XmlElement>() ?? []];

            if (objects.Count == 0)
            {
                findings.Add(new CheckFinding(severity, "brex", use, null, rule));
                continue;
            }

            foreach (XmlElement obj in objects)
            {
                string path = obj.GetAttribute("xpath");
                findings.Add(new CheckFinding(severity, "brex", use,
                    path.Length == 0 ? null : path, rule));
            }
        }

        foreach (XmlElement bad in report.SelectNodes("//xpathError")?.OfType<XmlElement>() ?? [])
        {
            findings.Add(new CheckFinding("warning", "brex",
                "A business rule could not be evaluated: " + Trim(bad.InnerText), null, null));
        }

        return code;
    }

    private void CheckPresentation(string xml, string schema, string title, List<CheckFinding> findings)
    {
        if (presentation is null)
        {
            // A server started without stylesheets. Worth saying once, as a
            // warning: nothing is wrong with the module, it just has no page.
            findings.Add(new CheckFinding("warning", "render",
                "This server has no presentation stylesheets, so this module has no page preview.",
                null, null));
            return;
        }

        if (!presentation.CanPresent(schema))
        {
            findings.Add(new CheckFinding("warning", "render",
                $"There is no presentation stylesheet for '{schema}' objects, so this module has no page preview.",
                null, null));
            return;
        }

        try
        {
            using PresentationFo fo = presentation.TransformToFo(xml, schema, title);
        }
        catch (Exception e)
        {
            findings.Add(new CheckFinding("error", "render",
                "This module cannot be laid out: " + e.Message, null, null));
        }
    }

    private static string Trim(string? value) =>
        string.Join(' ', (value ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
