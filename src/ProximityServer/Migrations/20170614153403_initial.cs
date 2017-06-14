using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ProximityServer.Migrations
{
    public partial class initial : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Followers",
                columns: table => new
                {
                    DbId = table.Column<int>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Initialized = table.Column<bool>(nullable: false),
                    IpAddress = table.Column<byte[]>(maxLength: 16, nullable: false),
                    LastRefreshTime = table.Column<DateTime>(nullable: false),
                    NeighborPort = table.Column<int>(nullable: true),
                    NetworkId = table.Column<byte[]>(maxLength: 32, nullable: false),
                    PrimaryPort = table.Column<int>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Followers", x => x.DbId);
                });

            migrationBuilder.CreateTable(
                name: "Neighbors",
                columns: table => new
                {
                    DbId = table.Column<int>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Initialized = table.Column<bool>(nullable: false),
                    IpAddress = table.Column<byte[]>(maxLength: 16, nullable: false),
                    LastRefreshTime = table.Column<DateTime>(nullable: false),
                    LocationLatitude = table.Column<decimal>(type: "decimal(9,6)", nullable: false),
                    LocationLongitude = table.Column<decimal>(type: "decimal(9,6)", nullable: false),
                    NeighborPort = table.Column<int>(nullable: true),
                    NetworkId = table.Column<byte[]>(maxLength: 32, nullable: false),
                    PrimaryPort = table.Column<int>(nullable: false),
                    SharedActivities = table.Column<int>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Neighbors", x => x.DbId);
                });

            migrationBuilder.CreateTable(
                name: "NeighborActivities",
                columns: table => new
                {
                    DbId = table.Column<int>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ActivityId = table.Column<uint>(nullable: false),
                    ExpirationTime = table.Column<DateTime>(nullable: false),
                    ExtraData = table.Column<string>(maxLength: 2048, nullable: false),
                    LocationLatitude = table.Column<decimal>(type: "decimal(9,6)", nullable: false),
                    LocationLongitude = table.Column<decimal>(type: "decimal(9,6)", nullable: false),
                    OwnerIdentityId = table.Column<byte[]>(maxLength: 32, nullable: false),
                    OwnerProfileServerId = table.Column<byte[]>(maxLength: 32, nullable: false),
                    OwnerProfileServerIpAddress = table.Column<byte[]>(maxLength: 16, nullable: false),
                    OwnerProfileServerPrimaryPort = table.Column<ushort>(nullable: false),
                    OwnerPublicKey = table.Column<byte[]>(maxLength: 128, nullable: false),
                    PrecisionRadius = table.Column<uint>(nullable: false),
                    PrimaryServerId = table.Column<byte[]>(maxLength: 32, nullable: false),
                    Signature = table.Column<byte[]>(maxLength: 100, nullable: false),
                    StartTime = table.Column<DateTime>(nullable: false),
                    Type = table.Column<string>(maxLength: 64, nullable: false),
                    Version = table.Column<byte[]>(maxLength: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NeighborActivities", x => x.DbId);
                });

            migrationBuilder.CreateTable(
                name: "NeighborhoodActions",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AdditionalData = table.Column<string>(nullable: true),
                    ExecuteAfter = table.Column<DateTime>(nullable: true),
                    ServerId = table.Column<byte[]>(maxLength: 32, nullable: false),
                    TargetActivityId = table.Column<uint>(maxLength: 32, nullable: false),
                    TargetActivityOwnerId = table.Column<byte[]>(maxLength: 32, nullable: true),
                    Timestamp = table.Column<DateTime>(nullable: false),
                    Type = table.Column<int>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NeighborhoodActions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PrimaryActivities",
                columns: table => new
                {
                    DbId = table.Column<int>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ActivityId = table.Column<uint>(nullable: false),
                    ExpirationTime = table.Column<DateTime>(nullable: false),
                    ExtraData = table.Column<string>(maxLength: 2048, nullable: false),
                    LocationLatitude = table.Column<decimal>(type: "decimal(9,6)", nullable: false),
                    LocationLongitude = table.Column<decimal>(type: "decimal(9,6)", nullable: false),
                    OwnerIdentityId = table.Column<byte[]>(maxLength: 32, nullable: false),
                    OwnerProfileServerId = table.Column<byte[]>(maxLength: 32, nullable: false),
                    OwnerProfileServerIpAddress = table.Column<byte[]>(maxLength: 16, nullable: false),
                    OwnerProfileServerPrimaryPort = table.Column<ushort>(nullable: false),
                    OwnerPublicKey = table.Column<byte[]>(maxLength: 128, nullable: false),
                    PrecisionRadius = table.Column<uint>(nullable: false),
                    Signature = table.Column<byte[]>(maxLength: 100, nullable: false),
                    StartTime = table.Column<DateTime>(nullable: false),
                    Type = table.Column<string>(maxLength: 64, nullable: false),
                    Version = table.Column<byte[]>(maxLength: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrimaryActivities", x => x.DbId);
                });

            migrationBuilder.CreateTable(
                name: "Settings",
                columns: table => new
                {
                    Name = table.Column<string>(nullable: false),
                    Value = table.Column<string>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Settings", x => x.Name);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Followers_Initialized",
                table: "Followers",
                column: "Initialized");

            migrationBuilder.CreateIndex(
                name: "IX_Followers_LastRefreshTime",
                table: "Followers",
                column: "LastRefreshTime");

            migrationBuilder.CreateIndex(
                name: "IX_Followers_NetworkId",
                table: "Followers",
                column: "NetworkId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Followers_IpAddress_PrimaryPort",
                table: "Followers",
                columns: new[] { "IpAddress", "PrimaryPort" });

            migrationBuilder.CreateIndex(
                name: "IX_Neighbors_Initialized",
                table: "Neighbors",
                column: "Initialized");

            migrationBuilder.CreateIndex(
                name: "IX_Neighbors_LastRefreshTime",
                table: "Neighbors",
                column: "LastRefreshTime");

            migrationBuilder.CreateIndex(
                name: "IX_Neighbors_NetworkId",
                table: "Neighbors",
                column: "NetworkId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Neighbors_IpAddress_PrimaryPort",
                table: "Neighbors",
                columns: new[] { "IpAddress", "PrimaryPort" });

            migrationBuilder.CreateIndex(
                name: "IX_NeighborActivities_ExpirationTime",
                table: "NeighborActivities",
                column: "ExpirationTime");

            migrationBuilder.CreateIndex(
                name: "IX_NeighborActivities_ExtraData",
                table: "NeighborActivities",
                column: "ExtraData");

            migrationBuilder.CreateIndex(
                name: "IX_NeighborActivities_OwnerIdentityId",
                table: "NeighborActivities",
                column: "OwnerIdentityId");

            migrationBuilder.CreateIndex(
                name: "IX_NeighborActivities_StartTime",
                table: "NeighborActivities",
                column: "StartTime");

            migrationBuilder.CreateIndex(
                name: "IX_NeighborActivities_Type",
                table: "NeighborActivities",
                column: "Type");

            migrationBuilder.CreateIndex(
                name: "IX_NeighborActivities_ActivityId_OwnerIdentityId",
                table: "NeighborActivities",
                columns: new[] { "ActivityId", "OwnerIdentityId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NeighborActivities_LocationLatitude_LocationLongitude_PrecisionRadius",
                table: "NeighborActivities",
                columns: new[] { "LocationLatitude", "LocationLongitude", "PrecisionRadius" });

            migrationBuilder.CreateIndex(
                name: "IX_NeighborActivities_ExpirationTime_StartTime_LocationLatitude_LocationLongitude_PrecisionRadius_Type_OwnerIdentityId",
                table: "NeighborActivities",
                columns: new[] { "ExpirationTime", "StartTime", "LocationLatitude", "LocationLongitude", "PrecisionRadius", "Type", "OwnerIdentityId" });

            migrationBuilder.CreateIndex(
                name: "IX_NeighborhoodActions_ExecuteAfter",
                table: "NeighborhoodActions",
                column: "ExecuteAfter");

            migrationBuilder.CreateIndex(
                name: "IX_NeighborhoodActions_Id",
                table: "NeighborhoodActions",
                column: "Id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NeighborhoodActions_ServerId",
                table: "NeighborhoodActions",
                column: "ServerId");

            migrationBuilder.CreateIndex(
                name: "IX_NeighborhoodActions_Timestamp",
                table: "NeighborhoodActions",
                column: "Timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_NeighborhoodActions_Type",
                table: "NeighborhoodActions",
                column: "Type");

            migrationBuilder.CreateIndex(
                name: "IX_NeighborhoodActions_TargetActivityId_TargetActivityOwnerId",
                table: "NeighborhoodActions",
                columns: new[] { "TargetActivityId", "TargetActivityOwnerId" });

            migrationBuilder.CreateIndex(
                name: "IX_NeighborhoodActions_ServerId_Type_TargetActivityId_TargetActivityOwnerId",
                table: "NeighborhoodActions",
                columns: new[] { "ServerId", "Type", "TargetActivityId", "TargetActivityOwnerId" });

            migrationBuilder.CreateIndex(
                name: "IX_PrimaryActivities_ExpirationTime",
                table: "PrimaryActivities",
                column: "ExpirationTime");

            migrationBuilder.CreateIndex(
                name: "IX_PrimaryActivities_ExtraData",
                table: "PrimaryActivities",
                column: "ExtraData");

            migrationBuilder.CreateIndex(
                name: "IX_PrimaryActivities_OwnerIdentityId",
                table: "PrimaryActivities",
                column: "OwnerIdentityId");

            migrationBuilder.CreateIndex(
                name: "IX_PrimaryActivities_StartTime",
                table: "PrimaryActivities",
                column: "StartTime");

            migrationBuilder.CreateIndex(
                name: "IX_PrimaryActivities_Type",
                table: "PrimaryActivities",
                column: "Type");

            migrationBuilder.CreateIndex(
                name: "IX_PrimaryActivities_ActivityId_OwnerIdentityId",
                table: "PrimaryActivities",
                columns: new[] { "ActivityId", "OwnerIdentityId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PrimaryActivities_LocationLatitude_LocationLongitude_PrecisionRadius",
                table: "PrimaryActivities",
                columns: new[] { "LocationLatitude", "LocationLongitude", "PrecisionRadius" });

            migrationBuilder.CreateIndex(
                name: "IX_PrimaryActivities_ExpirationTime_StartTime_LocationLatitude_LocationLongitude_PrecisionRadius_Type_OwnerIdentityId",
                table: "PrimaryActivities",
                columns: new[] { "ExpirationTime", "StartTime", "LocationLatitude", "LocationLongitude", "PrecisionRadius", "Type", "OwnerIdentityId" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Followers");

            migrationBuilder.DropTable(
                name: "Neighbors");

            migrationBuilder.DropTable(
                name: "NeighborActivities");

            migrationBuilder.DropTable(
                name: "NeighborhoodActions");

            migrationBuilder.DropTable(
                name: "PrimaryActivities");

            migrationBuilder.DropTable(
                name: "Settings");
        }
    }
}
