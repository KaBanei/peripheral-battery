using System;
using System.Runtime.InteropServices;
class ClickIt {
  [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] static extern void mouse_event(uint f, uint dx, uint dy, uint d, UIntPtr e);
  static void Main(string[] a) {
    int x = int.Parse(a[0]), y = int.Parse(a[1]);
    SetCursorPos(x, y);
    System.Threading.Thread.Sleep(250);
    mouse_event(0x0002, 0, 0, 0, UIntPtr.Zero);
    System.Threading.Thread.Sleep(80);
    mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero);
    Console.WriteLine("clicked " + x + "," + y);
  }
}
