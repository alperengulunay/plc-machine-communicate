﻿//------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated from a template.
//
//     Manual changes to this file may cause unexpected behavior in your application.
//     Manual changes to this file will be overwritten if the code is regenerated.
// </auto-generated>
//------------------------------------------------------------------------------

namespace ERN028_MakinaVeriTopalamaFormsApp
{
    using System;
    using System.Data.Entity;
    using System.Data.Entity.Infrastructure;
    
    public partial class ernamas_dijitalEntities : DbContext
    {
        public ernamas_dijitalEntities()
            : base("name=ernamas_dijitalEntities")
        {
        }
    
        protected override void OnModelCreating(DbModelBuilder modelBuilder)
        {
            throw new UnintentionalCodeFirstException();
        }
    
        public virtual DbSet<MachineChangeLogs> MachineChangeLogs { get; set; }
        public virtual DbSet<MachineCurrentStatus> MachineCurrentStatus { get; set; }
        public virtual DbSet<MachineDowntimeReports> MachineDowntimeReports { get; set; }
        public virtual DbSet<MachineFaultTypes> MachineFaultTypes { get; set; }
        public virtual DbSet<MachineHistoryStatus> MachineHistoryStatus { get; set; }
        public virtual DbSet<MachineLossQualities> MachineLossQualities { get; set; }
        public virtual DbSet<Machines> Machines { get; set; }
        public virtual DbSet<MachineStatusTypes> MachineStatusTypes { get; set; }
        public virtual DbSet<MachineProductionDatas> MachineProductionDatas { get; set; }
        public virtual DbSet<MachinesCycleTimeHistories> MachinesCycleTimeHistories { get; set; }
    }
}
