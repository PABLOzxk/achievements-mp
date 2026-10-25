namespace AchievementsMP.Data.Migrations;

/// <summary>Uma alteração de esquema. Versões são sequenciais e nunca editadas depois de publicadas.</summary>
public sealed record Migration(int Version, string Name, string Sql);
