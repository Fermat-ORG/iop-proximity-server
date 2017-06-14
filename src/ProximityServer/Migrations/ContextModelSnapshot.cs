using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using ProximityServer.Data;
using ProximityServer.Data.Models;

namespace ProximityServer.Migrations
{
    [DbContext(typeof(Context))]
    partial class ContextModelSnapshot : ModelSnapshot
    {
        protected override void BuildModel(ModelBuilder modelBuilder)
        {
            modelBuilder
                .HasAnnotation("ProductVersion", "1.1.1");

            modelBuilder.Entity("ProximityServer.Data.Models.Follower", b =>
                {
                    b.Property<int>("DbId")
                        .ValueGeneratedOnAdd();

                    b.Property<bool>("Initialized");

                    b.Property<byte[]>("IpAddress")
                        .IsRequired()
                        .HasMaxLength(16);

                    b.Property<DateTime>("LastRefreshTime");

                    b.Property<int?>("NeighborPort");

                    b.Property<byte[]>("NetworkId")
                        .IsRequired()
                        .HasMaxLength(32);

                    b.Property<int>("PrimaryPort");

                    b.HasKey("DbId");

                    b.HasIndex("Initialized");

                    b.HasIndex("LastRefreshTime");

                    b.HasIndex("NetworkId")
                        .IsUnique();

                    b.HasIndex("IpAddress", "PrimaryPort");

                    b.ToTable("Followers");
                });

            modelBuilder.Entity("ProximityServer.Data.Models.Neighbor", b =>
                {
                    b.Property<int>("DbId")
                        .ValueGeneratedOnAdd();

                    b.Property<bool>("Initialized");

                    b.Property<byte[]>("IpAddress")
                        .IsRequired()
                        .HasMaxLength(16);

                    b.Property<DateTime>("LastRefreshTime");

                    b.Property<decimal>("LocationLatitude")
                        .HasColumnType("decimal(9,6)");

                    b.Property<decimal>("LocationLongitude")
                        .HasColumnType("decimal(9,6)");

                    b.Property<int?>("NeighborPort");

                    b.Property<byte[]>("NetworkId")
                        .IsRequired()
                        .HasMaxLength(32);

                    b.Property<int>("PrimaryPort");

                    b.Property<int>("SharedActivities");

                    b.HasKey("DbId");

                    b.HasIndex("Initialized");

                    b.HasIndex("LastRefreshTime");

                    b.HasIndex("NetworkId")
                        .IsUnique();

                    b.HasIndex("IpAddress", "PrimaryPort");

                    b.ToTable("Neighbors");
                });

            modelBuilder.Entity("ProximityServer.Data.Models.NeighborActivity", b =>
                {
                    b.Property<int>("DbId")
                        .ValueGeneratedOnAdd();

                    b.Property<uint>("ActivityId");

                    b.Property<DateTime>("ExpirationTime");

                    b.Property<string>("ExtraData")
                        .IsRequired()
                        .HasMaxLength(2048);

                    b.Property<decimal>("LocationLatitude")
                        .HasColumnType("decimal(9,6)");

                    b.Property<decimal>("LocationLongitude")
                        .HasColumnType("decimal(9,6)");

                    b.Property<byte[]>("OwnerIdentityId")
                        .IsRequired()
                        .HasMaxLength(32);

                    b.Property<byte[]>("OwnerProfileServerId")
                        .IsRequired()
                        .HasMaxLength(32);

                    b.Property<byte[]>("OwnerProfileServerIpAddress")
                        .IsRequired()
                        .HasMaxLength(16);

                    b.Property<ushort>("OwnerProfileServerPrimaryPort");

                    b.Property<byte[]>("OwnerPublicKey")
                        .IsRequired()
                        .HasMaxLength(128);

                    b.Property<uint>("PrecisionRadius");

                    b.Property<byte[]>("PrimaryServerId")
                        .IsRequired()
                        .HasMaxLength(32);

                    b.Property<byte[]>("Signature")
                        .IsRequired()
                        .HasMaxLength(100);

                    b.Property<DateTime>("StartTime");

                    b.Property<string>("Type")
                        .IsRequired()
                        .HasMaxLength(64);

                    b.Property<byte[]>("Version")
                        .IsRequired()
                        .HasMaxLength(3);

                    b.HasKey("DbId");

                    b.HasIndex("ExpirationTime");

                    b.HasIndex("ExtraData");

                    b.HasIndex("OwnerIdentityId");

                    b.HasIndex("StartTime");

                    b.HasIndex("Type");

                    b.HasIndex("ActivityId", "OwnerIdentityId")
                        .IsUnique();

                    b.HasIndex("LocationLatitude", "LocationLongitude", "PrecisionRadius");

                    b.HasIndex("ExpirationTime", "StartTime", "LocationLatitude", "LocationLongitude", "PrecisionRadius", "Type", "OwnerIdentityId");

                    b.ToTable("NeighborActivities");
                });

            modelBuilder.Entity("ProximityServer.Data.Models.NeighborhoodAction", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd();

                    b.Property<string>("AdditionalData");

                    b.Property<DateTime?>("ExecuteAfter");

                    b.Property<byte[]>("ServerId")
                        .IsRequired()
                        .HasMaxLength(32);

                    b.Property<uint>("TargetActivityId")
                        .HasMaxLength(32);

                    b.Property<byte[]>("TargetActivityOwnerId")
                        .HasMaxLength(32);

                    b.Property<DateTime>("Timestamp");

                    b.Property<int>("Type");

                    b.HasKey("Id");

                    b.HasIndex("ExecuteAfter");

                    b.HasIndex("Id")
                        .IsUnique();

                    b.HasIndex("ServerId");

                    b.HasIndex("Timestamp");

                    b.HasIndex("Type");

                    b.HasIndex("TargetActivityId", "TargetActivityOwnerId");

                    b.HasIndex("ServerId", "Type", "TargetActivityId", "TargetActivityOwnerId");

                    b.ToTable("NeighborhoodActions");
                });

            modelBuilder.Entity("ProximityServer.Data.Models.PrimaryActivity", b =>
                {
                    b.Property<int>("DbId")
                        .ValueGeneratedOnAdd();

                    b.Property<uint>("ActivityId");

                    b.Property<DateTime>("ExpirationTime");

                    b.Property<string>("ExtraData")
                        .IsRequired()
                        .HasMaxLength(2048);

                    b.Property<decimal>("LocationLatitude")
                        .HasColumnType("decimal(9,6)");

                    b.Property<decimal>("LocationLongitude")
                        .HasColumnType("decimal(9,6)");

                    b.Property<byte[]>("OwnerIdentityId")
                        .IsRequired()
                        .HasMaxLength(32);

                    b.Property<byte[]>("OwnerProfileServerId")
                        .IsRequired()
                        .HasMaxLength(32);

                    b.Property<byte[]>("OwnerProfileServerIpAddress")
                        .IsRequired()
                        .HasMaxLength(16);

                    b.Property<ushort>("OwnerProfileServerPrimaryPort");

                    b.Property<byte[]>("OwnerPublicKey")
                        .IsRequired()
                        .HasMaxLength(128);

                    b.Property<uint>("PrecisionRadius");

                    b.Property<byte[]>("Signature")
                        .IsRequired()
                        .HasMaxLength(100);

                    b.Property<DateTime>("StartTime");

                    b.Property<string>("Type")
                        .IsRequired()
                        .HasMaxLength(64);

                    b.Property<byte[]>("Version")
                        .IsRequired()
                        .HasMaxLength(3);

                    b.HasKey("DbId");

                    b.HasIndex("ExpirationTime");

                    b.HasIndex("ExtraData");

                    b.HasIndex("OwnerIdentityId");

                    b.HasIndex("StartTime");

                    b.HasIndex("Type");

                    b.HasIndex("ActivityId", "OwnerIdentityId")
                        .IsUnique();

                    b.HasIndex("LocationLatitude", "LocationLongitude", "PrecisionRadius");

                    b.HasIndex("ExpirationTime", "StartTime", "LocationLatitude", "LocationLongitude", "PrecisionRadius", "Type", "OwnerIdentityId");

                    b.ToTable("PrimaryActivities");
                });

            modelBuilder.Entity("ProximityServer.Data.Models.Setting", b =>
                {
                    b.Property<string>("Name")
                        .ValueGeneratedOnAdd();

                    b.Property<string>("Value")
                        .IsRequired();

                    b.HasKey("Name");

                    b.ToTable("Settings");
                });
        }
    }
}
