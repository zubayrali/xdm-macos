using System;
using Microsoft.Data.Sqlite;
using System.IO;
using TraceLog;

namespace XDM.Core.DataAccess
{
    public static class DataImportExport
    {
        public static bool CopyToFile(SqliteConnection sql, string file)
        {
            try
            {
                var cs = $"Data Source={file}";
                using var dest = new SqliteConnection(cs);
                dest.Open();
                sql.BackupDatabase(dest, "main", "main");
                return true;
            }
            catch (Exception ex)
            {
                Log.Debug(ex, ex.Message);
                return false;
            }
        }

        public static bool CopyFromFile(SqliteConnection sql, string file)
        {
            try
            {
                using var attachCmd = new SqliteCommand($"ATTACH '{file}' as db", sql);
                attachCmd.ExecuteNonQuery();
                var tx = sql.BeginTransaction();
                try
                {
                    using var mergeCmd = new SqliteCommand($"INSERT OR IGNORE INTO downloads SELECT * FROM db.downloads", sql, tx);
                    mergeCmd.ExecuteNonQuery();
                    tx.Commit();
                }
                catch (Exception ex)
                {
                    tx.Rollback();
                    Log.Debug("Error during merge insert, performing rollback!!");
                    Log.Debug(ex, ex.Message);
                }
                using var detachCmd = new SqliteCommand($"DETACH db", sql);
                detachCmd.ExecuteNonQuery();
                return true;
            }
            catch (Exception ex)
            {
                Log.Debug(ex, ex.Message);
                return false;
            }
        }

        //public bool CopyDB(SQLiteConnection sourceDB, SQLiteConnection targetDB, )
        //{
        //    try
        //    {
        //        using var attachCmd = new SQLiteCommand($"ATTACH '{file}' as db", db);
        //        attachCmd.ExecuteNonQuery();
        //        var tx = db.BeginTransaction();
        //        try
        //        {
        //            using var mergeCmd = new SQLiteCommand($"INSERT OR IGNORE INTO downloads SELECT * FROM db.downloads", db);
        //            mergeCmd.ExecuteNonQuery();
        //            tx.Commit();
        //        }
        //        catch (Exception ex)
        //        {
        //            tx.Rollback();
        //            Log.Debug("Error during merge insert, performing rollback!!");
        //            Log.Debug(ex, ex.Message);
        //        }
        //        using var detachCmd = new SQLiteCommand($"DETACH db", db);
        //        detachCmd.ExecuteNonQuery();
        //        return true;
        //    }
        //    catch (Exception ex)
        //    {
        //        Log.Debug(ex, ex.Message);
        //        return false;
        //    }
        //}
    }
}
