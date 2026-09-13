namespace Personalaffe.Domain;

/// <summary>
/// The four focused areas of the workspace, each of which can be enabled or
/// disabled independently (<c>CONTEXT.md</c>, Application).
/// </summary>
/// <remarks>
/// <para>
/// The word is <em>application</em> and the wire spells it that way: the
/// contract's values are <c>scratchpad</c>, <c>knowledge</c>, <c>tasks</c> and
/// <c>files</c>, and a permission is per application and not per endpoint. The
/// type carries <c>Workspace</c> in front of it for one reason, which is a fact
/// about C# and not about the product: <c>Personalaffe.Application</c> is a
/// layer of this codebase, and a type called <c>Application</c> would be
/// ambiguous with that namespace in every file that names both.
/// </para>
/// <para>
/// A closed set of four, not a registry. An application is a folder in each
/// layer (<c>docs/codebase.md</c>) and a fifth one would be a decision
/// somebody makes, a case added here, and a migration — which is the right
/// amount of friction for adding a place the owner's content lives.
/// </para>
/// </remarks>
public enum WorkspaceApplication
{
    /// <summary>Temporary plain text, kept for cross-device use.</summary>
    Scratchpad,

    /// <summary>Lasting personal knowledge, as Markdown pages in a hierarchy.</summary>
    Knowledge,

    /// <summary>Personal commitments in named, manually ordered lists.</summary>
    Tasks,

    /// <summary>Stored personal files in a folder hierarchy.</summary>
    Files,
}

/// <summary>What a caller may do in one application.</summary>
/// <remarks>
/// Three values and no more. "Read" that could also delete, or a per-endpoint
/// grid, would be a permission model nobody can hold in their head — and the
/// thing being protected is one person's workspace, not a company's.
/// </remarks>
public enum Permission
{
    /// <summary>Nothing. The application is not this caller's to see.</summary>
    None,

    /// <summary>Read it, and change nothing.</summary>
    Read,

    /// <summary>Read it and write it, deletion included.</summary>
    ReadWrite,
}
