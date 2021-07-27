//  Copyright (c) 2021 Demerzel Solutions Limited
//  This file is part of the Nethermind library.
// 
//  The Nethermind library is free software: you can redistribute it and/or modify
//  it under the terms of the GNU Lesser General Public License as published by
//  the Free Software Foundation, either version 3 of the License, or
//  (at your option) any later version.
// 
//  The Nethermind library is distributed in the hope that it will be useful,
//  but WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
//  GNU Lesser General Public License for more details.
// 
//  You should have received a copy of the GNU Lesser General Public License
//  along with the Nethermind. If not, see <http://www.gnu.org/licenses/>.
// 

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Versioning;

namespace Nethermind.Plugin.CLI
{
    public static class CreateMacPlugin
    {
        public static string CreatePlugin(string pluginName, string className)
        {
            string execPath = "src/Nethermind/Nethermind.Plugin.CLI/CreateMacPlugin.sh";
            var asm = Assembly.GetExecutingAssembly();
            var b = asm.CustomAttributes.FirstOrDefault(a => a.AttributeType == typeof(TargetFrameworkAttribute));
            var strFramework = b.NamedArguments[0].TypedValue.Value;
            Console.WriteLine("here here");
            Console.WriteLine(strFramework);

            ProcessStartInfo startInfo = new ProcessStartInfo()
            {
                FileName = execPath,
                Arguments = pluginName + " " + className
            };
            
            Process proc = new Process()
            {
                StartInfo = startInfo,
            };
            proc.Start();
            proc.WaitForExit();
            return "Created Plugin";
        }
    }
}
