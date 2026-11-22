using System;
using System.Globalization;

namespace Orbiq.Services;

/// <summary>Безопасный разборщик выражений: не выполняет пользовательский текст как код.</summary>
public sealed class ExpressionEvaluator
{
    private string _text = string.Empty;
    private int _position;

    public bool UseDegrees { get; set; } = true;

    public double Evaluate(string expression)
    {
        _text = PrepareExpression(expression.Replace(',', '.').Replace("×", "*").Replace("÷", "/").Replace("−", "-"));
        _position = 0;
        double value = ParseExpression();
        SkipSpaces();
        if (_position != _text.Length) throw new FormatException("Лишние символы в выражении.");
        if (double.IsNaN(value) || double.IsInfinity(value)) throw new ArithmeticException("Результат не определён.");
        return value;
    }

    private static string PrepareExpression(string expression)
    {
        int openCount = 0;
        for (int i = 0; i < expression.Length; i++)
        {
            if (expression[i] == '(')
            {
                openCount++;
            }
            else if (expression[i] == ')')
            {
                openCount--;
                if (openCount < 0)
                    throw new FormatException("Лишняя закрывающая скобка. Проверь выражение.");

                int next = i + 1;
                while (next < expression.Length && char.IsWhiteSpace(expression[next])) next++;
                if (next < expression.Length)
                {
                    char following = expression[next];
                    if (char.IsDigit(following) || following == '.' || following == '(' || char.IsLetter(following))
                        throw new FormatException("После закрывающей скобки поставь знак операции. Например: (2+3) × 4.");
                }
            }
        }

        // Если выражение заканчивается незакрытой группой, считаем, что закрывающие
        // скобки пользователь просто не допечатал. Закрываем только в самом конце.
        if (openCount > 0)
            expression += new string(')', openCount);

        return expression;
    }

    private double ParseExpression()
    {
        double value = ParseTerm();
        while (true)
        {
            SkipSpaces();
            if (Match('+')) value += ParseTerm();
            else if (Match('-')) value -= ParseTerm();
            else return value;
        }
    }

    private double ParseTerm()
    {
        double value = ParseUnary();
        while (true)
        {
            SkipSpaces();
            if (Match('*')) value *= ParseUnary();
            else if (Match('/'))
            {
                double divisor = ParseUnary();
                if (Math.Abs(divisor) < double.Epsilon) throw new DivideByZeroException("На ноль делить нельзя.");
                value /= divisor;
            }
            else if (CanStartImplicitFactor())
            {
                value *= ParseUnary();
            }
            else return value;
        }
    }

    private double ParseUnary()
    {
        SkipSpaces();
        if (Match('+')) return ParseUnary();
        if (Match('-')) return -ParseUnary();
        return ParsePower();
    }

    private double ParsePower()
    {
        double value = ParsePostfix();
        SkipSpaces();
        if (Match('^')) value = Math.Pow(value, ParseUnary());
        return value;
    }

    private double ParsePostfix()
    {
        double value = ParsePrimary();
        while (true)
        {
            SkipSpaces();
            if (Match('%')) value /= 100.0;
            else if (Match('!')) value = Factorial(value);
            else return value;
        }
    }

    private double ParsePrimary()
    {
        SkipSpaces();
        if (Match('('))
        {
            double inner = ParseExpression();
            SkipSpaces();
            if (!Match(')')) throw new FormatException("Не хватает закрывающей скобки.");
            return inner;
        }

        if (_position < _text.Length && char.IsLetter(_text[_position]))
            return ParseIdentifierOrFunction();

        int start = _position;
        bool dotSeen = false;
        while (_position < _text.Length)
        {
            char c = _text[_position];
            if (char.IsDigit(c)) { _position++; continue; }
            if (c == '.' && !dotSeen) { dotSeen = true; _position++; continue; }
            break;
        }

        if (start == _position) throw new FormatException("Ожидалось число.");
        string numberText = _text[start.._position];
        if (!double.TryParse(numberText, NumberStyles.Float, CultureInfo.InvariantCulture, out double number))
            throw new FormatException("Некорректное число.");
        return number;
    }

    private double ParseIdentifierOrFunction()
    {
        int start = _position;
        while (_position < _text.Length && char.IsLetter(_text[_position])) _position++;
        string name = _text[start.._position].ToLowerInvariant();
        if (name == "pi") return Math.PI;
        if (name == "e") return Math.E;

        SkipSpaces();
        if (!Match('(')) throw new FormatException($"После функции «{name}» нужны скобки.");
        double argument = ParseExpression();
        SkipSpaces();
        if (!Match(')')) throw new FormatException("Не хватает закрывающей скобки после функции.");

        double angle = UseDegrees ? argument * Math.PI / 180.0 : argument;
        return name switch
        {
            "sqrt" => argument < 0 ? throw new ArithmeticException("Корень из отрицательного числа не определён.") : Math.Sqrt(argument),
            "sin" => Math.Sin(angle),
            "cos" => Math.Cos(angle),
            "tan" => Math.Tan(angle),
            "asin" => UseDegrees ? Math.Asin(argument) * 180.0 / Math.PI : Math.Asin(argument),
            "acos" => UseDegrees ? Math.Acos(argument) * 180.0 / Math.PI : Math.Acos(argument),
            "atan" => UseDegrees ? Math.Atan(argument) * 180.0 / Math.PI : Math.Atan(argument),
            "lg" or "log" => argument <= 0 ? throw new ArithmeticException("Логарифм определён только для положительных чисел.") : Math.Log10(argument),
            "ln" => argument <= 0 ? throw new ArithmeticException("Натуральный логарифм определён только для положительных чисел.") : Math.Log(argument),
            _ => throw new FormatException($"Неизвестная функция: {name}.")
        };
    }

    private static double Factorial(double value)
    {
        if (value < 0 || value > 170 || Math.Abs(value - Math.Round(value)) > 0.000000001)
            throw new ArithmeticException("Факториал доступен для целых чисел от 0 до 170.");
        double result = 1;
        for (int i = 2; i <= (int)value; i++) result *= i;
        return result;
    }

    private bool CanStartImplicitFactor()
    {
        if (_position >= _text.Length) return false;
        char c = _text[_position];
        return c == '(' || char.IsDigit(c) || c == '.' || char.IsLetter(c);
    }

    private void SkipSpaces()
    {
        while (_position < _text.Length && char.IsWhiteSpace(_text[_position])) _position++;
    }

    private bool Match(char expected)
    {
        if (_position < _text.Length && _text[_position] == expected)
        {
            _position++;
            return true;
        }
        return false;
    }
}
