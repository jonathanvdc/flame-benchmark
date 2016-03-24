using System;

public struct Point {
    public Point(double x, double y) {
        X = x; Y = y;
    }
    public double X { get; private set; }
    public double Y { get; private set; }
}

public static class CSharpTest {

    private static Point AddByVal(Point a, Point b) {
        return new Point(a.X + b.Y, a.Y + b.X);
    }

    private static Point AddByRef(ref Point a, ref Point b) {
        return new Point(a.X + b.Y, a.Y + b.X);
    }

    private static void AddByOut(ref Point a, ref Point b, out Point c) {
        c = new Point(a.X + b.Y, a.Y + b.X);
    }

    private static void AddNaked(double ax, double ay, double bx, double by, out double cx, out double cy) {
        cx = ax + by; cy = ay + bx;
    }

    public static void Main() {
        int iterations = 1000000000;

        int t0, t1;

        // trigger runtime compilation if needed
        Point a = new Point(1, 1), b = new Point(1, 1);
        double ax = 1, ay = 1, bx = 1, by = 1;
        for (int i = 0; i < 1000; i++) {
            a = AddByVal(a, b);
            a = AddByRef(ref a, ref b);
            AddByOut(ref a, ref b, out a);
            AddNaked(ax, ay, bx, by, out ax, out ay);
        }

        a = new Point(1, 1); b = new Point(1, 1);
        t0 = Environment.TickCount;
        for (int i = 0; i < iterations; i++)
            a = AddByVal(a, b);
        t1 = Environment.TickCount;
        Console.Write("{0} {1} ", a.X, a.Y);
        Console.WriteLine("AddByVal: {0}", (t1 - t0));

        a = new Point(1, 1); b = new Point(1, 1);
        t0 = Environment.TickCount;
        for (int i = 0; i < iterations; i++)
            a = AddByRef(ref a, ref b);
        t1 = Environment.TickCount;
        Console.Write("{0} {1} ", a.X, a.Y);
        Console.WriteLine("AddByRef: {0}", (t1 - t0));

        a = new Point(1, 1); b = new Point(1, 1);
        t0 = Environment.TickCount;
        for (int i = 0; i < iterations; i++)
            AddByOut(ref a, ref b, out a);
        t1 = Environment.TickCount;
        Console.Write("{0} {1} ", a.X, a.Y);
        Console.WriteLine("AddByOut: {0}", (t1 - t0));

        ax = 1; ay = 1; bx = 1; by = 1;
        t0 = Environment.TickCount;
        for (int i = 0; i < iterations; i++)
            AddNaked(ax, ay, bx, by, out ax, out ay);
        t1 = Environment.TickCount;
        Console.Write("{0} {1} ", ax, ay);
        Console.WriteLine("AddNaked: {0}", (t1 - t0));
    }
}
