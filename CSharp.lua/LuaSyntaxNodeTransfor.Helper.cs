using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using CSharpLua.LuaAst;

namespace CSharpLua {
    public sealed partial class LuaSyntaxNodeTransfor {
        private Dictionary<ISymbol, string> localReservedNames_ = new Dictionary<ISymbol, string>();
        private int localMappingCounter_;

        private abstract class LuaSyntaxSearcher : CSharpSyntaxWalker {
            private sealed class FoundException : Exception {
            }
            protected void Found() {
                throw new FoundException();
            }

            public bool Find(SyntaxNode root) {
                try {
                    Visit(root);
                }
                catch(FoundException) {
                    return true;
                }
                return false;
            }
        }

        private sealed class LocalVarSearcher : LuaSyntaxSearcher {
            private string name_;

            public LocalVarSearcher(string name) {
                name_ = name;
            }

            public override void VisitParameter(ParameterSyntax node) {
                if(node.Identifier.ValueText == name_) {
                    Found();
                }
            }

            public override void VisitVariableDeclarator(VariableDeclaratorSyntax node) {
                if(node.Identifier.ValueText == name_) {
                    Found();
                }
            }
        }

        private bool IsLocalVarExists(string name, MethodDeclarationSyntax root) {
            LocalVarSearcher searcher = new LocalVarSearcher(name);
            return searcher.Find(root);
        }

        private string GetNewIdentifierName(string name, int index) {
            switch(index) {
                case 0:
                    return name;
                case 1:
                    return name.FirstLetterToUpper();
                case 2:
                    return name + "_";
                case 3:
                    return "_" + name;
                default:
                    return name + (index - 4);
            }
        }

        private SyntaxNode FindParent(SyntaxNode node, SyntaxKind kind) {
            var parent = node.Parent;
            while(true) {
                if(parent.IsKind(kind)) {
                    return parent;
                }
                parent = parent.Parent;
            }
        }

        private string GetUniqueIdentifier(string name, SyntaxNode node, int index = 0) {
            var root = (MethodDeclarationSyntax)FindParent(node, SyntaxKind.MethodDeclaration);
            while(true) {
                string newName = GetNewIdentifierName(name, index);
                bool exists = IsLocalVarExists(newName, root);
                if(!exists) {
                    return newName;
                }
                ++index;
            }
        }

        private bool CheckReservedWord(ref string name, SyntaxNode node) {
            if(LuaSyntaxNode.IsReservedWord(name)) {
                name = GetUniqueIdentifier(name, node, 1);
                AddReservedMapping(name, node);
            }
            return false;
        }

        private void AddReservedMapping(string name, SyntaxNode node) {
            ISymbol symbol = semanticModel_.GetDeclaredSymbol(node);
            Contract.Assert(symbol != null);
            localReservedNames_.Add(symbol, name);
        }

        private void CheckParameterName(ref LuaParameterSyntax parameter, ParameterSyntax node) {
            string name = parameter.Identifier.ValueText;
            bool isReserved = CheckReservedWord(ref name, node);
            if(isReserved) {
                parameter = new LuaParameterSyntax(new LuaIdentifierNameSyntax(name));
            }
        }

        private void CheckVariableDeclaratorName(ref LuaIdentifierNameSyntax identifierName, VariableDeclaratorSyntax node) {
            string name = identifierName.ValueText;
            bool isReserved = CheckReservedWord(ref name, node);
            if(isReserved) {
                identifierName = new LuaIdentifierNameSyntax(name);
            }
        }

        private void CheckReservedWord(ref string name, ISymbol symbol) {
            if(LuaSyntaxNode.IsReservedWord(name)) {
                name = localReservedNames_[symbol];
            }
        }

        private sealed class ContinueSearcher : LuaSyntaxSearcher {
            public override void VisitContinueStatement(ContinueStatementSyntax node) {
                Found();
            }
        }

        private bool IsContinueExists(SyntaxNode node) {
            ContinueSearcher searcher = new ContinueSearcher();
            return searcher.Find(node);
        }

        private sealed class ReturnStatementSearcher : LuaSyntaxSearcher {
            public override void VisitReturnStatement(ReturnStatementSyntax node) {
                Found();
            }
        }

        private bool IsReturnExists(SyntaxNode node) {
            ReturnStatementSearcher searcher = new ReturnStatementSearcher();
            return searcher.Find(node);
        }

        private int GetCaseLabelIndex(GotoStatementSyntax node) {
            var switchStatement = (SwitchStatementSyntax)FindParent(node, SyntaxKind.SwitchStatement);
            int index = 0;
            foreach(var section in switchStatement.Sections) {
               bool isFound = section.Labels.Any(i => {
                    if(i.IsKind(SyntaxKind.CaseSwitchLabel)) {
                        var label = (CaseSwitchLabelSyntax)i;
                        if(label.Value.ToString() == node.Expression.ToString()) {
                            return true;
                        }
                    }
                    return false;
                });
                if(isFound) {
                    return index;
                }
            }
            throw new InvalidOperationException();
        }
    }
}
