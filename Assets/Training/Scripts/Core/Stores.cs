using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace ConstructionVR.Training
{
    public enum UserRole { Learner = 0, Instructor = 1, Administrator = 2 }

    [Serializable]
    public class UserAccount
    {
        public string login;
        public string name;
        public UserRole role;
        public string salt;
        public string hash;
    }

    /// <summary>
    /// The people who may log in. Kept in a file on the device (users.json in the persistent data folder) with
    /// salted password hashes. Three accounts exist from the first start so the trainer can be used at once;
    /// their passwords are meant to be changed by the administrator (the management screens come later).
    /// </summary>
    public static class UserStore
    {
        [Serializable]
        private class File_
        {
            public List<UserAccount> users = new List<UserAccount>();
        }

        private static File_ s_file;

        private static string FilePath => Path.Combine(Application.persistentDataPath, "users.json");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() => s_file = null;

        public static IReadOnlyList<UserAccount> Users
        {
            get
            {
                Load();
                return s_file.users;
            }
        }

        public static bool TryLogin(string login, string password, out UserAccount account)
        {
            Load();
            account = s_file.users.Find(u => string.Equals(u.login, login?.Trim(), StringComparison.OrdinalIgnoreCase));
            if (account == null || account.hash != Hash(password ?? string.Empty, account.salt))
            {
                account = null;
                return false;
            }

            return true;
        }

        public static UserAccount Add(string login, string name, UserRole role, string password)
        {
            Load();
            s_file.users.RemoveAll(u => string.Equals(u.login, login, StringComparison.OrdinalIgnoreCase));
            string salt = Guid.NewGuid().ToString("N").Substring(0, 12);
            var account = new UserAccount { login = login, name = name, role = role, salt = salt, hash = Hash(password, salt) };
            s_file.users.Add(account);
            Save();
            return account;
        }

        private static void Load()
        {
            if (s_file != null)
            {
                return;
            }

            s_file = new File_();
            try
            {
                if (File.Exists(FilePath))
                {
                    JsonUtility.FromJsonOverwrite(File.ReadAllText(FilePath), s_file);
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[Training] users.json could not be read: " + e.Message);
            }

            if (s_file.users.Count == 0)
            {
                Add("stajyor", "Stajyor payvandchi", UserRole.Learner, "1234");
                Add("instruktor", "Instruktor", UserRole.Instructor, "1234");
                Add("admin", "Administrator", UserRole.Administrator, "1234");
            }
        }

        private static void Save()
        {
            try
            {
                File.WriteAllText(FilePath, JsonUtility.ToJson(s_file, true));
            }
            catch (Exception e)
            {
                Debug.LogError("[Training] users.json could not be written: " + e.Message);
            }
        }

        private static string Hash(string password, string salt)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(salt + ":" + password));
                var sb = new StringBuilder(bytes.Length * 2);
                foreach (byte b in bytes)
                {
                    sb.Append(b.ToString("x2"));
                }

                return sb.ToString();
            }
        }
    }

    [Serializable]
    public class AttemptResult
    {
        public string login;
        public string name;
        public string practice;
        public string date;
        public float seconds;
        public float safety;
        public float technique;
        public float quality;
        public float time;
        public float total;
        public bool passed;
        public bool critical;
        public string variant;
        public float[] stageSeconds;
        public TrainingEvent[] events;
    }

    /// <summary>Finished attempts, newest last (results.json in the persistent data folder): the instructor's list.</summary>
    public static class ResultsStore
    {
        [Serializable]
        private class File_
        {
            public List<AttemptResult> results = new List<AttemptResult>();
        }

        private static File_ s_file;

        private static string FilePath => Path.Combine(Application.persistentDataPath, "results.json");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() => s_file = null;

        public static IReadOnlyList<AttemptResult> All
        {
            get
            {
                Load();
                return s_file.results;
            }
        }

        public static void Add(AttemptResult result)
        {
            Load();
            s_file.results.Add(result);
            try
            {
                File.WriteAllText(FilePath, JsonUtility.ToJson(s_file, true));
            }
            catch (Exception e)
            {
                Debug.LogError("[Training] results.json could not be written: " + e.Message);
            }
        }

        private static void Load()
        {
            if (s_file != null)
            {
                return;
            }

            s_file = new File_();
            try
            {
                if (File.Exists(FilePath))
                {
                    JsonUtility.FromJsonOverwrite(File.ReadAllText(FilePath), s_file);
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[Training] results.json could not be read: " + e.Message);
            }
        }
    }
}
