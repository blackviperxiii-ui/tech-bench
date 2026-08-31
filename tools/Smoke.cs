using System;
namespace TechBench {
  static class Smoke {
    static int Main() {
      var kb = new KbIndex();
      kb.Load();
      Console.WriteLine(kb.Status);
      foreach (string q in new[] { "F68", "P425", "07", "kaeser sigma" }) {
        var hits = kb.Search(q, "ALL");
        Console.WriteLine("Q=" + q + " n=" + hits.Count);
        if (hits.Count > 0) Console.WriteLine("  first: " + hits[0]);
      }
      return 0;
    }
  }
}
