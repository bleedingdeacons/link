namespace TheBleedingDeacons.Intergroup.Link.Models;

/// <summary>The subject and body a forward starts from. See <see cref="Forwarding"/>.</summary>
public sealed record ForwardDraft(string Subject, string Body);
