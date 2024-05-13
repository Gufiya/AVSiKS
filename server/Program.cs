using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Npgsql;
using System.Linq;
using System.Collections.Generic;

namespace server
{
    class Program
    {
        static TcpListener listener = null;
        static string connectionString = "Host=localhost;Port=5432;Username=adm;Password=adm;Database=MyDb";
        static Dictionary<string, string> credentials; //данные пользователей
        //static Dictionary<string, TcpClient> clients = new Dictionary<string, TcpClient>(); //подключенные клиенты
        static Dictionary<string, TcpClient> authclients = new Dictionary<string, TcpClient>(); //аутентифицированные клиенты

        static void Main(string[] args)
        {
            credentials = ReadCredentialsFromDatabase();

            listener = new TcpListener(IPAddress.Any, 8000); //прослушивание адреса
            listener.Start();
            Console.WriteLine("Сервер запущен. Ожидание подключений...");

            while (true)
            {
                TcpClient client = listener.AcceptTcpClient();
                Thread clientThread = new Thread(() => HandleClient(client));
                clientThread.Start();
            }
        }

        static Dictionary<string, string> ReadCredentialsFromDatabase()
        {
            Dictionary<string, string> credentials = new Dictionary<string, string>();

            using (NpgsqlConnection connection = new NpgsqlConnection(connectionString))
            {
                connection.Open();
                string sql = "SELECT username, password FROM users";
                using (NpgsqlCommand command = new NpgsqlCommand(sql, connection))
                {
                    using (NpgsqlDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            string username = reader.GetString(0);
                            string password = reader.GetString(1);
                            Console.WriteLine($"Получены данные из базы данных: {username}, {password}");
                            credentials[username] = password;
                        }
                    }
                }
            }
            return credentials;
        }

        static void HandleClient(TcpClient client)
        {
            NetworkStream stream = client.GetStream();
            byte[] buffer = new byte[1024]; //буфер для чтения данных
            int bytesRead; //количесвто прочитанных байтов

            while (true)
            {
                bytesRead = 0;
                try
                {
                    bytesRead = stream.Read(buffer, 0, buffer.Length);
                }
                catch //выход при ошибке
                {
                    break;
                }
                if (bytesRead == 0) //выход, если клиент отключился
                {
                    break;
                }

                string message = Encoding.UTF8.GetString(buffer, 0, bytesRead); //преобразование полученных данных в стринг

                if (message.StartsWith("REGISTER"))
                {
                    string[] registerData = message.Split(' '); //REGISTER логин пароль
                    if (registerData.Length == 3)               //    0      1     2
                    {
                        string username = registerData[1];
                        string password = registerData[2];

                        if (!credentials.ContainsKey(username))
                        {
                            authclients[username] = client;
                            RegisterUser(username, password);
                            SendMessage(client, "REGISTER_SUCCESS");
                            SendMessage(client, "AUTH_SUCCESS");
                            Console.WriteLine($"{username} успешно зарегистрирован и авторизован");
                        }
                        else
                        {
                            SendMessage(client, $"Ошибка регистрации. Пользователь {username} уже существует");
                            return;
                        }
                    }
                    else
                    {
                        SendMessage(client, "Неверный формат запроса на регистрацию");
                    }
                }
                else if (message.StartsWith("AUTH"))//else if (!isAuthenticated && message.StartsWith("AUTH"))
                {
                    string[] authData = message.Split(' ');
                    if (authData.Length == 3)
                    {
                        string username = authData[1];
                        string password = authData[2];
                        if (credentials.ContainsKey(username) && credentials[username] == password)
                        {
                            if (authclients.ContainsKey(username))
                            {
                                SendMessage(client, $"Ошибка аутентификации. Пользователь {username} уже подключен");
                                Console.WriteLine($"{username} уже подключен");
                                client.Close();
                                return;
                            }
                            authclients[username] = client;

                            SendMessage(client, "AUTH_SUCCES");
                            Console.WriteLine($"{username} успешно аутентифицирован");
                        }
                        else if (credentials.ContainsKey(username) && credentials[username] != password)
                        {
                            SendMessage(client, $"Неправильный пароль пользователя {username}");
                            //Console.WriteLine($"{username} ошибка аутентификации");
                            client.Close();
                        }
                        else
                        {
                            SendMessage(client, $"Пользователь с логином {username} не существует");
                            //Console.WriteLine($"{username} не существует);
                            client.Close();
                        }
                    }
                }

                Console.WriteLine($"Получено сообщение от клиента {message}");
                foreach (var kvp in authclients) //перебор элементов
                {
                    if (kvp.Value != client && kvp.Value.Connected)
                    {
                        if (!message.StartsWith("AUTH") && !message.StartsWith("REGISTER"))
                        {
                            SendMessage(kvp.Value, message);
                        }
                    }
                }

            }
           
            var keyV = authclients.FirstOrDefault(x => x.Value == client).Key;
            Console.WriteLine($"Клиент {keyV} отключился");
            foreach (var kvp in authclients) //перебор элементов
            {
                if (kvp.Value == client)
                {
                    
                    //Console.WriteLine($"Клиент {user} отключился");
                    authclients.Remove(kvp.Key);
                    
                }
             
            }
            client.Close();
        }

        static void RegisterUser(string username, string password)
        {
            using (NpgsqlConnection connection = new NpgsqlConnection(connectionString))
            {
                connection.Open();

                string sql = "INSERT INTO users (username, password) VALUES (@username, @password)";
                using (NpgsqlCommand command = new NpgsqlCommand(sql, connection))
                {
                    command.Parameters.AddWithValue("username", username);
                    command.Parameters.AddWithValue("password", password);
                    command.ExecuteNonQuery();
                }
                credentials[username] = password;
            }
        }

        static void SendMessage(TcpClient client, string message)
        {
            NetworkStream stream = client.GetStream();
            byte[] buffer = Encoding.UTF8.GetBytes(message);
            stream.Write(buffer, 0, buffer.Length);
        }
    }
}
