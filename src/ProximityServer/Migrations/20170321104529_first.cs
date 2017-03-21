using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ProximityServer.Migrations
{
    public partial class first : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Follower",
                columns: table => new
                {
                    DbId = table.Column<int>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    FollowerId = table.Column<byte[]>(maxLength: 32, nullable: false),
                    IpAddress = table.Column<string>(nullable: false),
                    LastRefreshTime = table.Column<DateTime>(nullable: true),
                    NeighborPort = table.Column<int>(nullable: true),
                    PrimaryPort = table.Column<int>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Follower", x => x.DbId);
                });

            migrationBuilder.CreateTable(
                name: "Neighbor",
                columns: table => new
                {
                    DbId = table.Column<int>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    IpAddress = table.Column<string>(nullable: false),
                    LastRefreshTime = table.Column<DateTime>(nullable: true),
                    LocationLatitude = table.Column<decimal>(type: "decimal(9,6)", nullable: false),
                    LocationLongitude = table.Column<decimal>(type: "decimal(9,6)", nullable: false),
                    NeighborId = table.Column<byte[]>(maxLength: 32, nullable: false),
                    NeighborPort = table.Column<int>(nullable: true),
                    PrimaryPort = table.Column<int>(nullable: false),
                    SharedActivities = table.Column<int>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Neighbor", x => x.DbId);
                });

            migrationBuilder.CreateTable(
                name: "NeighborActivity",
                columns: table => new
                {
                    DbId = table.Column<int>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ActivityId = table.Column<int>(nullable: false),
                    ExpirationTime = table.Column<DateTime>(nullable: false),
                    ExtraData = table.Column<string>(maxLength: 2048, nullable: false),
                    LocationLatitude = table.Column<decimal>(type: "decimal(9,6)", nullable: false),
                    LocationLongitude = table.Column<decimal>(type: "decimal(9,6)", nullable: false),
                    OwnerIdentityId = table.Column<byte[]>(maxLength: 32, nullable: false),
                    OwnerProfileServerId = table.Column<byte[]>(maxLength: 32, nullable: false),
                    OwnerProfileServerIpAddress = table.Column<byte[]>(maxLength: 16, nullable: false),
                    OwnerProfileServerPrimaryPort = table.Column<ushort>(nullable: false),
                    PrecisionRadius = table.Column<uint>(nullable: false),
                    PrimaryServerId = table.Column<byte[]>(maxLength: 32, nullable: false),
                    StartTime = table.Column<DateTime>(nullable: false),
                    Type = table.Column<string>(maxLength: 64, nullable: false),
                    Version = table.Column<byte[]>(maxLength: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NeighborActivity", x => x.DbId);
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
                    TargetActivityId = table.Column<int>(maxLength: 32, nullable: true),
                    TargetActivityOwnerId = table.Column<byte[]>(maxLength: 32, nullable: true),
                    Timestamp = table.Column<DateTime>(nullable: false),
                    Type = table.Column<int>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NeighborhoodActions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PrimaryActivity",
                columns: table => new
                {
                    DbId = table.Column<int>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ActivityId = table.Column<int>(nullable: false),
                    ExpirationTime = table.Column<DateTime>(nullable: false),
                    ExtraData = table.Column<string>(maxLength: 2048, nullable: false),
                    LocationLatitude = table.Column<decimal>(type: "decimal(9,6)", nullable: false),
                    LocationLongitude = table.Column<decimal>(type: "decimal(9,6)", nullable: false),
                    OwnerIdentityId = table.Column<byte[]>(maxLength: 32, nullable: false),
                    OwnerProfileServerId = table.Column<byte[]>(maxLength: 32, nullable: false),
                    OwnerProfileServerIpAddress = table.Column<byte[]>(maxLength: 16, nullable: false),
                    OwnerProfileServerPrimaryPort = table.Column<ushort>(nullable: false),
                    PrecisionRadius = table.Column<uint>(nullable: false),
                    StartTime = table.Column<DateTime>(nullable: false),
                    Type = table.Column<string>(maxLength: 64, nullable: false),
                    Version = table.Column<byte[]>(maxLength: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrimaryActivity", x => x.DbId);
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
                name: "IX_Follower_FollowerId",
                table: "Follower",
                column: "FollowerId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Follower_LastRefreshTime",
                table: "Follower",
                column: "LastRefreshTime");

            migrationBuilder.CreateIndex(
                name: "IX_Follower_IpAddress_PrimaryPort",
                table: "Follower",
                columns: new[] { "IpAddress", "PrimaryPort" });

            migrationBuilder.CreateIndex(
                name: "IX_Neighbor_LastRefreshTime",
                table: "Neighbor",
                column: "LastRefreshTime");

            migrationBuilder.CreateIndex(
                name: "IX_Neighbor_NeighborId",
                table: "Neighbor",
                column: "NeighborId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Neighbor_IpAddress_PrimaryPort",
                table: "Neighbor",
                columns: new[] { "IpAddress", "PrimaryPort" });

            migrationBuilder.CreateIndex(
                name: "IX_NeighborActivity_ExpirationTime",
                table: "NeighborActivity",
                column: "ExpirationTime");

            migrationBuilder.CreateIndex(
                name: "IX_NeighborActivity_ExtraData",
                table: "NeighborActivity",
                column: "ExtraData");

            migrationBuilder.CreateIndex(
                name: "IX_NeighborActivity_OwnerIdentityId",
                table: "NeighborActivity",
                column: "OwnerIdentityId");

            migrationBuilder.CreateIndex(
                name: "IX_NeighborActivity_StartTime",
                table: "NeighborActivity",
                column: "StartTime");

            migrationBuilder.CreateIndex(
                name: "IX_NeighborActivity_Type",
                table: "NeighborActivity",
                column: "Type");

            migrationBuilder.CreateIndex(
                name: "IX_NeighborActivity_ActivityId_OwnerIdentityId",
                table: "NeighborActivity",
                columns: new[] { "ActivityId", "OwnerIdentityId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NeighborActivity_LocationLatitude_LocationLongitude_PrecisionRadius",
                table: "NeighborActivity",
                columns: new[] { "LocationLatitude", "LocationLongitude", "PrecisionRadius" });

            migrationBuilder.CreateIndex(
                name: "IX_NeighborActivity_ExpirationTime_StartTime_LocationLatitude_LocationLongitude_PrecisionRadius_Type_OwnerIdentityId",
                table: "NeighborActivity",
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
                name: "IX_PrimaryActivity_ExpirationTime",
                table: "PrimaryActivity",
                column: "ExpirationTime");

            migrationBuilder.CreateIndex(
                name: "IX_PrimaryActivity_ExtraData",
                table: "PrimaryActivity",
                column: "ExtraData");

            migrationBuilder.CreateIndex(
                name: "IX_PrimaryActivity_OwnerIdentityId",
                table: "PrimaryActivity",
                column: "OwnerIdentityId");

            migrationBuilder.CreateIndex(
                name: "IX_PrimaryActivity_StartTime",
                table: "PrimaryActivity",
                column: "StartTime");

            migrationBuilder.CreateIndex(
                name: "IX_PrimaryActivity_Type",
                table: "PrimaryActivity",
                column: "Type");

            migrationBuilder.CreateIndex(
                name: "IX_PrimaryActivity_ActivityId_OwnerIdentityId",
                table: "PrimaryActivity",
                columns: new[] { "ActivityId", "OwnerIdentityId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PrimaryActivity_LocationLatitude_LocationLongitude_PrecisionRadius",
                table: "PrimaryActivity",
                columns: new[] { "LocationLatitude", "LocationLongitude", "PrecisionRadius" });

            migrationBuilder.CreateIndex(
                name: "IX_PrimaryActivity_ExpirationTime_StartTime_LocationLatitude_LocationLongitude_PrecisionRadius_Type_OwnerIdentityId",
                table: "PrimaryActivity",
                columns: new[] { "ExpirationTime", "StartTime", "LocationLatitude", "LocationLongitude", "PrecisionRadius", "Type", "OwnerIdentityId" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Follower");

            migrationBuilder.DropTable(
                name: "Neighbor");

            migrationBuilder.DropTable(
                name: "NeighborActivity");

            migrationBuilder.DropTable(
                name: "NeighborhoodActions");

            migrationBuilder.DropTable(
                name: "PrimaryActivity");

            migrationBuilder.DropTable(
                name: "Settings");
        }
    }
}
