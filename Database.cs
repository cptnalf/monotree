using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Text;

namespace Monotree
{
    /// <summary>Database connection to monotone's database.</summary>
    class Database
    {
        /// <summary>Filename of the database.</summary>
        string filename;

        /// <summary>Database connection.</summary>
        DbConnection con;

        /// <summary>Creates a database connection.</summary>
        /// <param name="filename">Filename of the database to connect to.</param>
        public Database(string filename)
        {
            this.filename = filename;
            Init();
        }

        /// <summary>Gets the filename of the database.</summary>
        /// <value>Gets the filename of the database.</value>
        public string FileName
        {
            get { return filename; }
        }

        /// <summary>Initializes a database connection.</summary>
        private void Init()
        {
            DbProviderFactory dataFactory = DbProviderFactories.GetFactory(Properties.Settings.Default["ProviderName"].ToString());
            con = dataFactory.CreateConnection();
            con.ConnectionString = Properties.Settings.Default["ConnectionString"].ToString() + filename;
            con.Open();
        }

        /// <summary>Decodes UTF8-encoded values.</summary>
        /// <param name="byteArray">UTF8-encoded value.</param>
        /// <returns>Decoded value.</returns>
        private string UTF8Decode(byte[] byteArray)
        {
            UTF8Encoding encoder = new UTF8Encoding();
            return encoder.GetString(byteArray).TrimEnd('\0');
        }

        /// <summary>Escapes a string to be used in a SQL statement.</summary>
        /// <param name="s">String to escape.</param>
        /// <returns>Escaped string.</returns>
        private string SQLEscape(string s)
        {
            return s.Replace("'", "\\'");
        }

        /// <summary>Gets branch names.</summary>
        /// <returns>Branch names.</returns>
        public List<string> GetBranchNames()
        {
            DbCommand cmd = con.CreateCommand();
            cmd.CommandText = "select distinct value from revision_certs where name = 'branch' order by value";
            cmd.CommandType = CommandType.Text;

            DbDataReader r = cmd.ExecuteReader();
            List<string> names = new List<string>();
            while (r.Read())
            {
                byte[] buffer = new byte[256];
                r.GetBytes(0, 0, buffer, 0, buffer.Length);
                names.Add(UTF8Decode(buffer));
            }
            return names;
        }

        /// <summary>Gets the latest revisions for a branch.</summary>
        /// <param name="branch">Branch to get revisions from.</param>
        /// <param name="limit">Maximum number of revisions to get.</param>
        /// <returns>Revisions.</returns>
        public Dictionary<string, Revision> GetLatestRevisionIDs(string branch, int limit)
        {
            DbCommand cmd = con.CreateCommand();
            cmd.CommandText = "select distinct r.id from revisions as r inner join revision_certs as rc on (r.id = rc.id) where rc.name = 'branch' and rc.value like '" + SQLEscape(branch) + "' order by r.ROWID desc limit " + limit;
            cmd.CommandType = CommandType.Text;

            DbDataReader r = cmd.ExecuteReader();
            Dictionary<string, Revision> revs = new Dictionary<string, Revision>(limit);
            while (r.Read())
            {
                byte[] buffer1 = new byte[256];
                r.GetBytes(0, 0, buffer1, 0, buffer1.Length);
                string id = UTF8Decode(buffer1);

                revs.Add(id, new Revision(id, branch));
            }

            foreach (KeyValuePair<string, Revision> rev in revs)
            {
                cmd = con.CreateCommand();
                cmd.CommandText = "select distinct rc1.value, rc2.value, rc3.value from revisions as r inner join revision_certs as rc1 on (r.id = rc1.id) inner join revision_certs as rc2 on (r.id = rc2.id) inner join revision_certs as rc3 on (r.id = rc3.id) where r.id = '" + SQLEscape(rev.Key) + "' and rc1.name = 'author' and rc2.name = 'date' and rc3.name = 'changelog'";
                cmd.CommandType = CommandType.Text;

                r = cmd.ExecuteReader();
                r.Read();

                byte[] buffer1 = new byte[256];
                r.GetBytes(0, 0, buffer1, 0, buffer1.Length);
                string author = UTF8Decode(buffer1);

                byte[] buffer2 = new byte[256];
                r.GetBytes(1, 0, buffer2, 0, buffer2.Length);
                string date = UTF8Decode(buffer2);

                byte[] buffer3 = new byte[256];
                r.GetBytes(2, 0, buffer3, 0, buffer3.Length);
                string log = UTF8Decode(buffer3);

                rev.Value.Author = author.Trim();
                rev.Value.Date = DateTime.Parse(date);
                rev.Value.Log = log.Trim();

                cmd = con.CreateCommand();
                cmd.CommandText = "select distinct parent from revision_ancestry where child = '" + SQLEscape(rev.Key) + "'";
                cmd.CommandType = CommandType.Text;

                r = cmd.ExecuteReader();
                while (r.Read())
                {
                    buffer1 = new byte[256];
                    r.GetBytes(0, 0, buffer1, 0, buffer1.Length);
                    string id = UTF8Decode(buffer1);

                    if (id != "")
                        rev.Value.Parents.Add(id);
                }
            }

            return revs;
        }

        /// <summary>Gets a revision.</summary>
        /// <param name="id">Unique identifier.</param>
        /// <returns>Revision or null if the unique identifier is invalid.</returns>
        public Revision GetRevision(string id)
        {
            DbCommand cmd = con.CreateCommand();
            cmd.CommandText = "select distinct rc1.value, rc2.value, rc3.value, rc4.value from revisions as r inner join revision_certs as rc1 on (r.id = rc1.id) inner join revision_certs as rc2 on (r.id = rc2.id) inner join revision_certs as rc3 on (r.id = rc3.id) inner join revision_certs as rc4 on (r.id = rc4.id) where r.id = '" + SQLEscape(id) + "' and rc1.name = 'author' and rc2.name = 'date' and rc3.name = 'changelog' and rc4.name = 'branch'";
            cmd.CommandType = CommandType.Text;

            DbDataReader r = cmd.ExecuteReader();
            if (r.Read())
            {
                byte[] buffer1 = new byte[256];
                r.GetBytes(0, 0, buffer1, 0, buffer1.Length);
                string author = UTF8Decode(buffer1);

                byte[] buffer2 = new byte[256];
                r.GetBytes(1, 0, buffer2, 0, buffer2.Length);
                string date = UTF8Decode(buffer2);

                byte[] buffer3 = new byte[256];
                r.GetBytes(2, 0, buffer3, 0, buffer3.Length);
                string log = UTF8Decode(buffer3);

                byte[] buffer4 = new byte[256];
                r.GetBytes(3, 0, buffer4, 0, buffer4.Length);
                string branch = UTF8Decode(buffer4);

                return new Revision(id, branch.Trim(), author.Trim(), date, log.Trim());
            }

            return null;
        }

        /// <summary>Compresses a database.</summary>
        /// <remarks>When a database is compressed all data is deleted which is not used by Monotree.</remarks>
        public void Compress()
        {
            string[] tables = new string[] { "heights", "rosters", "roster_deltas", "public_keys", "manifest_certs", "manifest_deltas", "manifests", "files", "file_deltas" };

            foreach (string table in tables)
            {
                DbCommand cmd = con.CreateCommand();
                cmd.CommandText = "drop table if exists " + table;
                cmd.CommandType = CommandType.Text;
                cmd.ExecuteNonQuery();
            }

            DbCommand cmd2 = con.CreateCommand();
            cmd2.CommandText = "vacuum";
            cmd2.CommandType = CommandType.Text;
            cmd2.ExecuteNonQuery();
        }
    }
}
