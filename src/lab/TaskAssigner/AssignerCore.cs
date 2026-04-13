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

using System;
using System.Collections.Generic;
using System.Linq;
using Google.OrTools.LinearSolver;

namespace TaskAssigner
{
    public class AssignerCore
    {
        private readonly Solver solver;
        private readonly AssignmentDescription assignment;
        private readonly int[,] costs;
        private readonly Variable[,] variables;

        // TODO(zhangshuai.ds): Pass in 3 policies.
        public AssignerCore(Solver solver, AssignmentDescription assignment)
        {
            this.solver = solver ?? throw new ArgumentNullException(nameof(solver));
            this.assignment = assignment ?? throw new ArgumentNullException(nameof(assignment));
            this.costs = ComputeCosts(assignment);
            this.variables =
                this.solver.MakeBoolVarMatrix(assignment.Nodes.Count, assignment.Tasks.Count);
        }

        public AssignmentDescription Run()
        {
            this.AddObjective();
            this.AddConstraints();

            Solver.ResultStatus status = this.solver.Solve();
            if (status == Solver.ResultStatus.OPTIMAL)
            {
                var result = new AssignmentDescription();

                foreach (NodeDescription n in this.assignment.Nodes)
                {
                    result.Nodes.Add(n);
                }

                foreach (TaskDescription t in this.assignment.Tasks)
                {
                    result.Tasks.Add(t);
                }

                for (int i = 1; i < this.assignment.Nodes.Count; i++)
                {
                    for (int j = 0; j < this.assignment.Tasks.Count; j++)
                    {
                        if (this.variables[i, j].SolutionValue() > 0)
                        {
                            if (!result.Assignments.TryGetValue(i, out ISet<int> tasks))
                            {
                                tasks = new HashSet<int>();
                                result.Assignments.Add(i, tasks);
                            }

                            tasks.Add(j);
                            Console.WriteLine(
                                $"Task {j} assigned to node {i} with cost {this.costs[i, j]}.");
                        }
                    }
                }

                Console.WriteLine($"Total cost is {this.solver.Objective().Value()}");

                return result;
            }

            throw new NotImplementedException($"Not implemented for status: {status}");
        }

        protected static int[,] ComputeCosts(AssignmentDescription assignment)
        {
            int[,] costs = new int[assignment.Nodes.Count, assignment.Tasks.Count];
            for (int j = 0; j < assignment.Tasks.Count; j++)
            {
                costs[0, j] = 500;  // UNASSIGNED cost

                // Not assigned yet.
                if (assignment.Assignments[0].Contains(j))
                {
                    for (int i = 1; i < assignment.Nodes.Count; i++)
                    {
                        costs[i, j] = 0;  // No fee for assignment.
                    }
                }
                else
                {
                    for (int i = 1; i < assignment.Nodes.Count; i++)
                    {
                        if (assignment.Assignments[i].Contains(j))
                        {
                            costs[i, j] = 0;  // Don't move.
                        }
                        else
                        {
                            costs[i, j] = 200;  // Moving cost.
                        }
                    }
                }
            }

            return costs;
        }

        protected void AddObjective()
        {
            var movingCosts = new LinearExpr();
            for (int i = 0; i < this.assignment.Nodes.Count; i++)
            {
                for (int j = 0; j < this.assignment.Tasks.Count; j++)
                {
                    movingCosts += this.costs[i, j] * this.variables[i, j];
                }
            }

            // TODO(zhangshuai.ds): Computes it.
            var imbalanceCosts = new LinearExpr();

            // TODO(zhangshuai.ds): Add coefficients for the two costs.
            this.solver.Minimize(movingCosts + imbalanceCosts);
        }

        protected void AddConstraints()
        {
            // Each task is assigned to exactly one node (including UNASSIGNED virtual node).
            for (int j = 0; j < this.assignment.Tasks.Count; j++)
            {
                LinearExpr expr = Enumerable.Range(0, this.assignment.Nodes.Count)
                    .Select(i => this.variables[i, j])
                    .ToArray()
                    .Sum();
                this.solver.Add(expr == 1);
            }

            // Each worker cannot exceed its capacity.
            for (int i = 0; i < this.assignment.Nodes.Count; i++)
            {
                var cpuCores = new LinearExpr();
                var memoryMiB = new LinearExpr();

                for (int j = 0; j < this.assignment.Tasks.Count; j++)
                {
                    cpuCores += this.variables[i, j] * this.assignment.Tasks[j].CpuCores;
                    memoryMiB += this.variables[i, j] * this.assignment.Tasks[j].MemoryMiB;
                }

                this.solver.Add(cpuCores <= this.assignment.Nodes[i].CpuCores);
                this.solver.Add(memoryMiB <= this.assignment.Nodes[i].MemoryMiB);
            }
        }
    }
}
