﻿namespace Milyli.ScriptRunner.Data.Repositories
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using LinqToDB;
    using LinqToDB.Data;
    using Milyli.Framework.Repositories;
    using Milyli.ScriptRunner.Data.DataContexts;
    using Milyli.ScriptRunner.Data.Models;

    public enum JobActivationStatus
    {
        InvalidJob = -1,
        Idle = 0,
        Started = 1,
        AlreadyRunning = 2
    }

    public class JobScheduleRepository : BaseReadWriteRepository<InstanceDataContext, JobSchedule, int>, IJobScheduleRepository
    {
        // One day, in seconds
        private const int DEFAULT_MAX_OFFSET = 86400;

        public JobScheduleRepository(InstanceDataContext dataContext)
            : base(dataContext)
        {
        }

        // Returns a list of Jobs to run, filtered by NextExecution constrained to NextExecutionTimes between maxOffset seconds before runtime and runtime
        public List<JobSchedule> GetJobSchedules(DateTime runtime, int maxOffset)
        {
            var result = this.DataContext.JobSchedule.Where(s => s.NextExecutionTime <= runtime && runtime.AddSeconds(-1 * maxOffset) <= s.NextExecutionTime).ToList();
            return result;
        }

        public List<JobSchedule> GetJobSchedules(DateTime runtime)
        {
            return this.GetJobSchedules(runtime, DEFAULT_MAX_OFFSET);
        }

        public JobActivationStatus StartJob(JobSchedule schedule)
        {
            using (var connection = (DataConnection)this.DataContext)
            using (var transaction = connection.BeginTransaction(System.Data.IsolationLevel.RepeatableRead))
            {
                try
                {
                    var activationStatus = this.LockJobSchedule(schedule, transaction);
                    if (activationStatus == JobActivationStatus.Idle)
                    {
                        this.EnterRunningStatus(schedule);
                        transaction.Commit();
                        return JobActivationStatus.Started;
                    }
                    else
                    {
                        transaction.Rollback();
                        return activationStatus;
                    }
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
            }
        }

        /// <summary>
        /// Removes all jobs for a given relativity script.  Used in testing
        /// </summary>
        /// <param name="relativityScriptGuid">the GUID for the relativity script</param>
        /// <returns>the number of rows removed</returns>
        public int Delete(Guid relativityScriptGuid)
        {
            int result;
            using (var transaction = this.DataContext.BeginTransaction())
            {
                try
                {
                    transaction.DataConnection.Execute(
                        @"DELETE jh FROM JobHistory jh 
                            INNER JOIN JobSchedule js ON jh.JobScheduleId = js.JobScheduleId
                            WHERE js.RelativityScriptId = @relativityScriptId",
                        new DataParameter("relativityScriptId", relativityScriptGuid));
                    result = this.DataContext.JobSchedule.Where(s => s.RelativityScriptId == relativityScriptGuid).Delete();
                    transaction.Commit();
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
            }

            return result;
        }

        /// <summary>
        /// Ends a job run and updates the scheudle and the corresponding history.  This has a side-effect in that the CurrentJobHistory
        /// field is cleared upon commit
        /// </summary>
        /// <param name="jobSchedule">the job that is completing</param>
        public void FinishJob(JobSchedule jobSchedule)
        {
            jobSchedule.CurrentJobHistory.UpdateRuntime();
            jobSchedule.UpdateExecutionTimes();
            jobSchedule.JobStatus = (int)JobStatus.Idle;
            using (var transaction = this.DataContext.BeginTransaction())
            {
                try
                {
                    this.DataContext.Update(jobSchedule.CurrentJobHistory);
                    this.DataContext.Update(jobSchedule);
                    transaction.Commit();
                    jobSchedule.CurrentJobHistory = null;
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
            }
        }

        /// <summary>
        /// returns the most recent execution history for jobSchedule
        /// </summary>
        /// <param name="jobSchedule">The job to get the history for</param>
        /// <returns>a JobHistory record</returns>
        public JobHistory GetLastJobExecution(JobSchedule jobSchedule)
        {
            return this.DataContext.JobHistory
                .Take(1)
                .Where(h => h.JobScheduleId == jobSchedule.Id)
                .OrderByDescending(h => h.StartTime)
                .FirstOrDefault();
        }

        public List<JobHistory> GetJobHistory(JobSchedule jobSchedule)
        {
            return this.DataContext.JobHistory
                .Where(h => h.JobScheduleId == jobSchedule.Id)
                .OrderByDescending(h => h.StartTime).ToList();
        }

        // Critical section:
        // this method locks the schedule row for update, in order to coordinate job running.
        private JobActivationStatus LockJobSchedule(JobSchedule jobSchedule, DataConnectionTransaction transaction)
        {
            using (var reader = transaction.DataConnection.ExecuteReader(
                @"SELECT JobStatus
                            FROM JobSchedule WITH(UPDLOCK, ROWLOCK)
                            WHERE JobScheduleId = @jobScheduleId", new DataParameter("jobScheduleId", jobSchedule.Id)))
            {
                if (reader.Reader.Read())
                {
                    return reader.Reader.GetInt32(0) > 0 ? JobActivationStatus.AlreadyRunning : JobActivationStatus.Idle;
                }
                else
                {
                    return JobActivationStatus.InvalidJob;
                }
            }

        }

        private void EnterRunningStatus(JobSchedule jobSchedule)
        {
            jobSchedule.JobStatus = (int)JobStatus.Running;
            this.Update(jobSchedule);
            jobSchedule.CurrentJobHistory = this.AddHistoryEntry(jobSchedule);
        }

        private JobHistory AddHistoryEntry(JobSchedule jobSchedule)
        {
            var jobHistory = new JobHistory()
            {
                JobScheduleId = jobSchedule.Id,
            };
            jobHistory.Id = Convert.ToInt32(this.DataContext.InsertWithIdentity(jobHistory));
            return jobHistory;
        }
    }
}
