// Copyright (c) 2022 Zhang Shuai<zhangshuai.ustc@gmail.com>.
// All rights reserved.
//
// This file is part of OneDotNet.
//
// OneDotNet is free software: you can redistribute it and/or modify it under
// the terms of the GNU General Public License as published by the Free
// Software Foundation, either version 3 of the License, or (at your option)
// any later version.
//
// OneDotNet is distributed in the hope that it will be useful, but WITHOUT ANY
// WARRANTY; without even the implied warranty of MERCHANTABILITY or FITNESS
// FOR A PARTICULAR PURPOSE. See the GNU General Public License for more
// details.
//
// You should have received a copy of the GNU General Public License along with
// OneDotNet. If not, see <https://www.gnu.org/licenses/>.

using System.Collections.Generic;
using Google.OrTools.LinearSolver;

namespace TaskAssigner
{
    internal class Program
    {
        private static void Main()
        {
            using var solver = Solver.CreateSolver(
                "CBC_MIXED_INTEGER_PROGRAMMING");

            var assigner = new Assigner(solver);

            var assignment = new AssignmentDescription();
            assignment.Nodes.Add(new NodeDescription
            {
                Id = 0,
                CpuCores = int.MaxValue,
                MemoryMiB = int.MaxValue,
            });
            for (int i = 1; i <= 3; i++)
            {
                assignment.Nodes.Add(new NodeDescription
                {
                    Id = i,
                    CpuCores = 48,
                    MemoryMiB = 64 << 10,  // 64 GiB
                });
            }

            for (int i = 0; i < 13; i++)
            {
                assignment.Tasks.Add(new TaskDescription
                {
                    Id = i,
                    CpuCores = 8,
                    MemoryMiB = 32 << 10,  // 32 GiB, CPU:MEM=1:4
                });
            }

            for (int i = 0; i < 17; i++)
            {
                assignment.Tasks.Add(new TaskDescription
                {
                    Id = 13 + i,
                    CpuCores = 16,
                    MemoryMiB = 32 << 10,  // 16 GiB, CPU:MEM=1:2
                });
            }

            assignment.Assignments[0] = new HashSet<int>();
            for (int i = 0; i < assignment.Tasks.Count; i++)
            {
                assignment.Assignments[0].Add(i);
            }

            for (int i = 1; i < assignment.Nodes.Count; i++)
            {
                assignment.Assignments[i] = new HashSet<int>();
            }

            assignment = assigner.Solve(assignment);
        }
    }
}
