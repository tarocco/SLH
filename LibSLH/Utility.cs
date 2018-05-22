using System;

namespace LibSLH
{
    public static class Utility
    {
        public static string GetPassword()
        {
            var password = "";
            while (true)
            {
                var i = Console.ReadKey(true);
                if (i.Key == ConsoleKey.Enter)
                {
                    Console.WriteLine();
                    break;
                }
                if (i.Key == ConsoleKey.Backspace)
                {
                    if (password.Length > 0)
                    {
                        password.Remove(password.Length - 1);
                    }
                }
                else
                {
                    password += i.KeyChar;
                }
            }
            return password;
        }
    }
}