using System;
using Npgsql;

class Program
{
    static void Main()
    {
        string connString = "Host=aws-1-ap-southeast-1.pooler.supabase.com;Port=5432;Database=postgres;Username=postgres.tpmgujjlyuecnpgyzsor;Password=PioconTextile;SSL Mode=Require;Trust Server Certificate=true";
        Console.WriteLine("Listing Tables in Public Schema...");

        try
        {
            using (var conn = new NpgsqlConnection(connString))
            {
                conn.Open();
                
                string query = "SELECT table_name FROM information_schema.tables WHERE table_schema = 'public';";

                using (var cmd = new NpgsqlCommand(query, conn))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        Console.WriteLine($"Table: {reader.GetString(0)}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.ToString());
        }
    }
}
