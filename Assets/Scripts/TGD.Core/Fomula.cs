using System;
using System.Collections.Generic;
using System.Globalization;

namespace TGD.Core
{
    /// <summary>
    /// Lightweight expression evaluator used by design-time tooling and combat previews.
    /// Supports +, -, *, /, parenthesis, a handful of math helpers and variable substitution.
    /// </summary>
    public static class Formula
    {
        private enum TokenType
        {
            Number,
            Identifier,
            Operator,
            LeftParen,
            RightParen,
            Comma,
            Function
        }

        private readonly struct Token
        {
            public TokenType Type { get; }
            public string Value { get; }

            public Token(TokenType type, string value)
            {
                Type = type;
                Value = value;
            }
        }

        public static bool TryEvaluate(string expression, IReadOnlyDictionary<string, float> variables, out float value)
        {
            value = 0f;
            if (string.IsNullOrWhiteSpace(expression))
                return false;

            try
            {
                var tokens = Tokenize(expression);
                var rpn = ToReversePolish(tokens);
                value = EvaluateRpn(rpn, variables);
                return true;
            }
            catch
            {
                value = 0f;
                return false;
            }
        }

        public static float EvaluateOrDefault(string expression, IReadOnlyDictionary<string, float> variables, float fallback = 0f)
        {
            return TryEvaluate(expression, variables, out var result) ? result : fallback;
        }

        public static float Evaluate(string expression, IReadOnlyDictionary<string, float> variables)
        {
            if (!TryEvaluate(expression, variables, out var result))
                throw new FormatException($"Unable to evaluate expression '{expression}'.");
            return result;
        }

        private static List<Token> Tokenize(string expression)
        {
            var tokens = new List<Token>();
            int length = expression.Length;

            for (int i = 0; i < length;)
            {
                char c = expression[i];
                if (char.IsWhiteSpace(c))
                {
                    i++;
                    continue;
                }

                if (char.IsDigit(c) || c == '.')
                {
                    int start = i;
                    bool hasDot = c == '.';
                    i++;
                    while (i < length)
                    {
                        char ch = expression[i];
                        if (char.IsDigit(ch))
                        {
                            i++;
                            continue;
                        }
                        if (ch == '.' && !hasDot)
                        {
                            hasDot = true;
                            i++;
                            continue;
                        }
                        break;
                    }
                    tokens.Add(new Token(TokenType.Number, expression.Substring(start, i - start)));
                    continue;
                }

                if (char.IsLetter(c) || c == '_')
                {
                    int start = i;
                    i++;
                    while (i < length && (char.IsLetterOrDigit(expression[i]) || expression[i] == '_'))
                        i++;
                    string name = expression.Substring(start, i - start);
                    int j = i;
                    while (j < length && char.IsWhiteSpace(expression[j]))
                        j++;
                    bool isFunction = j < length && expression[j] == '(';
                    tokens.Add(new Token(isFunction ? TokenType.Function : TokenType.Identifier, name));
                    continue;
                }

                switch (c)
                {
                    case '+':
                    case '-':
                        {
                            bool unary = tokens.Count == 0 || tokens[^1].Type == TokenType.Operator ||
                                          tokens[^1].Type == TokenType.LeftParen || tokens[^1].Type == TokenType.Comma;
                            if (unary)
                            {
                                if (c == '-')
                                {
                                    tokens.Add(new Token(TokenType.Number, "0"));
                                    tokens.Add(new Token(TokenType.Operator, "-"));
                                }
                            }
                            else
                            {
                                tokens.Add(new Token(TokenType.Operator, c.ToString()));
                            }
                            i++;
                            break;
                        }
                    case '*':
                    case '/':
                        tokens.Add(new Token(TokenType.Operator, c.ToString()));
                        i++;
                        break;
                    case '(':
                        tokens.Add(new Token(TokenType.LeftParen, "("));
                        i++;
                        break;
                    case ')':
                        tokens.Add(new Token(TokenType.RightParen, ")"));
                        i++;
                        break;
                    case ',':
                        tokens.Add(new Token(TokenType.Comma, ","));
                        i++;
                        break;
                    default:
                        throw new FormatException($"Unexpected character '{c}' in expression.");
                }
            }

            return tokens;
        }

        private static List<Token> ToReversePolish(List<Token> tokens)
        {
            var output = new List<Token>();
            var stack = new Stack<Token>();

            foreach (var token in tokens)
            {
                switch (token.Type)
                {
                    case TokenType.Number:
                    case TokenType.Identifier:
                        output.Add(token);
                        break;
                    case TokenType.Function:
                        stack.Push(token);
                        break;
                    case TokenType.Operator:
                        while (stack.Count > 0 && stack.Peek().Type == TokenType.Operator &&
                               GetPrecedence(stack.Peek().Value) >= GetPrecedence(token.Value))
                        {
                            output.Add(stack.Pop());
                        }
                        stack.Push(token);
                        break;
                    case TokenType.LeftParen:
                        stack.Push(token);
                        break;
                    case TokenType.RightParen:
                        while (stack.Count > 0 && stack.Peek().Type != TokenType.LeftParen)
                        {
                            output.Add(stack.Pop());
                        }
                        if (stack.Count == 0)
                            throw new FormatException("Mismatched parentheses in expression.");
                        stack.Pop();
                        if (stack.Count > 0 && stack.Peek().Type == TokenType.Function)
                            output.Add(stack.Pop());
                        break;
                    case TokenType.Comma:
                        while (stack.Count > 0 && stack.Peek().Type != TokenType.LeftParen)
                        {
                            output.Add(stack.Pop());
                        }
                        if (stack.Count == 0 || stack.Peek().Type != TokenType.LeftParen)
                            throw new FormatException("Misplaced comma in expression.");
                        break;
                }
            }

            while (stack.Count > 0)
            {
                var top = stack.Pop();
                if (top.Type == TokenType.LeftParen || top.Type == TokenType.RightParen)
                    throw new FormatException("Mismatched parentheses in expression.");
                output.Add(top);
            }

            return output;
        }

        private static float EvaluateRpn(List<Token> rpn, IReadOnlyDictionary<string, float> variables)
        {
            var stack = new Stack<float>();

            foreach (var token in rpn)
            {
                switch (token.Type)
                {
                    case TokenType.Number:
                        stack.Push(float.Parse(token.Value, CultureInfo.InvariantCulture));
                        break;
                    case TokenType.Identifier:
                        stack.Push(LookupVariable(token.Value, variables));
                        break;
                    case TokenType.Operator:
                        if (stack.Count < 2)
                            throw new FormatException("Invalid expression (missing operands).");
                        float right = stack.Pop();
                        float left = stack.Pop();
                        stack.Push(ApplyOperator(token.Value, left, right));
                        break;
                    case TokenType.Function:
                        stack.Push(ApplyFunction(token.Value, stack));
                        break;
                    default:
                        throw new FormatException($"Unexpected token '{token.Value}'.");
                }
            }

            if (stack.Count != 1)
                throw new FormatException("Invalid expression (stack imbalance).");
            return stack.Pop();
        }

        private static float LookupVariable(string name, IReadOnlyDictionary<string, float> variables)
        {
            if (variables == null)
                return 0f;
            if (variables.TryGetValue(name, out var direct))
                return direct;

            string lower = name.ToLowerInvariant();
            foreach (var kvp in variables)
            {
                if (string.Equals(kvp.Key, name, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(kvp.Key, lower, StringComparison.OrdinalIgnoreCase))
                {
                    return kvp.Value;
                }
            }
            return 0f;
        }

        private static float ApplyOperator(string op, float left, float right)
        {
            return op switch
            {
                "+" => left + right,
                "-" => left - right,
                "*" => left * right,
                "/" => Math.Abs(right) < float.Epsilon ? 0f : left / right,
                _ => throw new FormatException($"Unsupported operator '{op}'.")
            };
        }

        private static float ApplyFunction(string name, Stack<float> stack)
        {
            string lower = name.ToLowerInvariant();
            switch (lower)
            {
                case "abs":
                    EnsureArgs(name, stack, 1);
                    return Math.Abs(stack.Pop());
                case "min":
                    EnsureArgs(name, stack, 2);
                    {
                        float b = stack.Pop();
                        float a = stack.Pop();
                        return Math.Min(a, b);
                    }
                case "max":
                    EnsureArgs(name, stack, 2);
                    {
                        float b = stack.Pop();
                        float a = stack.Pop();
                        return Math.Max(a, b);
                    }
                case "clamp":
                    EnsureArgs(name, stack, 3);
                    {
                        float max = stack.Pop();
                        float min = stack.Pop();
                        float value = stack.Pop();
                        return Math.Min(Math.Max(value, min), max);
                    }
                case "floor":
                    EnsureArgs(name, stack, 1);
                    return (float)Math.Floor(stack.Pop());
                case "ceil":
                    EnsureArgs(name, stack, 1);
                    return (float)Math.Ceiling(stack.Pop());
                case "round":
                    EnsureArgs(name, stack, 1);
                    return (float)Math.Round(stack.Pop());
                case "sqrt":
                    EnsureArgs(name, stack, 1);
                    return (float)Math.Sqrt(Math.Max(stack.Pop(), 0f));
                default:
                    throw new FormatException($"Unsupported function '{name}'.");
            }
        }

        private static void EnsureArgs(string name, Stack<float> stack, int count)
        {
            if (stack.Count < count)
                throw new FormatException($"Function '{name}' expects {count} argument(s).");
        }

        private static int GetPrecedence(string op)
        {
            return op switch
            {
                "+" => 1,
                "-" => 1,
                "*" => 2,
                "/" => 2,
                _ => 0
            };
        }
    }
}