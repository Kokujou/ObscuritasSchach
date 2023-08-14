using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System;
using System.Linq;
using System.Threading;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;

/*
    Todo-Liste:
        - Bauernumwandlung beim Gegner, Bevorzugung in der KI
        - KI Anpassen!
        - gegner. König setzt König matt | proof
        - gegnerische Rochade | proof!
    Schriftlich:
        - En Passant & Rochade ergänzen
*/
namespace Chess
{
    public class Player : MonoBehaviour
    {
        static Player instance;
        private static object thisLock = new object();
        public static GameObject[] Free, Enemies, SEnemy, Spezial, Schach;
        public static Material SelectedMat, EnemyMat, WeißMat, SchwarzMat, HoverMat;
        static GameObject Hover, Selection;
        GameObject LastHover;
        Material LastHoverMat;
        static List<KI.Schachzug> Zugverlauf = new List<KI.Schachzug>();
        public GUISkin guiskin;
        static PFigur toTransform;
        public static T Clone<T>(T source)
        {
            if (!typeof(T).IsSerializable)
            {
                throw new ArgumentException("The type must be serializable.", "source");
            }

            // Don't serialize a null object, simply return the default for that object
            if (System.Object.ReferenceEquals(source, null))
            {
                return default(T);
            }

            IFormatter formatter = new BinaryFormatter();
            Stream stream = new MemoryStream();
            using (stream)
            {
                formatter.Serialize(stream, source);
                stream.Seek(0, SeekOrigin.Begin);
                return (T)formatter.Deserialize(stream);
            }
        }
        public Camera CameraMain;
        public Camera CameraTransform;
        public float Schrittweite = 11.25f;
        public Vector3 BottomLeft = new Vector3(221.625f, 74.6f, 286.875f);
        KI enemyTurn;
        public static int ToIndex(List<PFigur> Set, GameObject go)
        {
            for (int i = 0; i < Set.Count; i++)
                if (Set[i].Objekt == go)
                    return i;
            return -1;
        }
        public static float Clamp(float min, float max, float value)
        {
            return (value < min) ? min : (value > max) ? max : value;
        }
        [Serializable]
        public class KI
        {
            public static List<List<State>> StateTree = new List<List<State>>();
            public static int[][] Root;
            public Spieler Player;
            public static bool TreeContainsField(int[][] Field)
            {
                for (int i = 0; i < StateTree.Count; i++)
                {
                    for (int j = 0; j < StateTree[i].Count; j++)
                    {
                        for (int k = 0; k < StateTree[i][j].Feld.Length; k++)
                        {
                            for (int l = 0; l < StateTree[i][j].Feld[k].Length; l++)
                            {
                                if (StateTree[i][j].Feld[k][l] != Field[k][l])
                                    return false;
                            }
                        }
                    }
                }
                return true;
            }
            public static void BuildNode(State node, int index, int pid, bool Turn, int level)
            {
                List<PFigur>[] FigurSets = new List<PFigur>[] { new List<PFigur>(), new List<PFigur>() };
                if (!node.isFinal)
                {
                    FigurSets[0] = node.wFiguren;
                    FigurSets[1] = node.sFiguren;
                    int activeSet = Convert.ToInt32(((!Convert.ToBoolean(pid) || Turn) && (Convert.ToBoolean(pid) || !Turn)));
                    foreach (PFigur Figur in FigurSets[activeSet])
                    {
                        Figur.Movement = Spieler.OnSelect(Figur, node.Feld, true);
                        // j=x, i=y; Top Left Corner
                        for (int i = 0; i < Figur.Movement.Field.Length; i++)
                        {
                            for (int j = 0; j < Figur.Movement.Field[i].Length; j++)
                            {
                                if (!new int[] { 0, 5, 6 }.Contains(Figur.Movement.Field[i][j]))
                                {
                                    State child = new State(new Schachzug(Figur, Figur.position, new PFigur.Position(j, i), Figur.Movement.Field[i][j]), node, node.Feld, Turn == true ? pid : (1 - pid));
                                    if (child.isFinalState())
                                    {
                                        node.isFinal = true;
                                    }
                                    else if (!TreeContainsField(child.Feld))
                                    {
                                        lock (thisLock)
                                        {
                                            StateTree[level + 1].Add(child);
                                            node.Childs.Add(StateTree[level + 1].Last());
                                        }

                                    }
                                }
                            }
                        }
                    }
                }
            }
            public static void FillStateTree(object param)
            {
                object[] args = new object[3];
                args = (object[])param;
                int pid = Convert.ToInt32(args[0]);
                int maxDepth = Convert.ToInt32(args[1]);
                State root = (State)args[2];
                StateTree.Add(new List<State> { root });
                bool Turn = true;
                for (int level = 0; level < maxDepth; level++, Turn = !Turn)
                {
                    if ((level > 0 && StateTree[level - 1].Count != 0) || level == 0)
                    {
                        StateTree.Add(new List<State>());
                        Thread[] threads = new Thread[StateTree[level].Count];
                        for (int n = 0; n < threads.Length; n++)
                        {
                            State node = StateTree[level][n];
                            threads[n] = new Thread(() => BuildNode(node, n, pid, Turn, level));
                            threads[n].Start();
                        }
                        bool running = true;
                        while (running)
                            for (int i = 0; i < threads.Length; i++)
                            {
                                if (threads[i].IsAlive)
                                    break;
                                else if (i == threads.Length - 1 && !threads[i].IsAlive)
                                    running = false;
                            }
                    }
                }
            }
            [Serializable]
            public class Schachzug
            {
                public PFigur Figur;
                public PFigur.Position newPos;
                public PFigur.Position oldPos;
                public int moveType;
                public Dictionary<PFigur.Position, int> XReplace = new Dictionary<PFigur.Position, int>();
                public Schachzug(PFigur figur, PFigur.Position pos1, PFigur.Position pos2, int movetype)
                {
                    if (figur.Objekt == null)
                        Figur = new PFigur(figur.Klasse, figur.position, figur.Color);
                    else
                        Figur = figur;
                    oldPos = pos1;
                    newPos = pos2;
                    moveType = movetype;
                    switch (movetype)
                    {
                        case 3:
                            XReplace.Add(new PFigur.Position(newPos.x, Figur.position.y), 0);
                            break;
                        case 4:
                            XReplace.Add(new PFigur.Position(((newPos.x - Figur.position.x) / Math.Abs(newPos.x - Figur.position.x)) + Figur.position.x, Figur.position.y), Figur.ToClassIndex() - 3);
                            break;
                    }
                }
                public override string ToString()
                {
                    int xPos = Convert.ToChar(Figur.position.x) + 65;
                    int newxPos = Convert.ToChar(newPos.x) + 65;
                    if (this != null)
                        switch (moveType)
                        {
                            case 1:
                                return Figur.Klasse.ToString() + " " + Figur.Color.ToString() + " zieht von " + oldPos.ToFieldPos() + " auf " + newPos.ToFieldPos();
                            case 2:
                                return Figur.Klasse.ToString() + " " + Figur.Color.ToString() + " auf " + oldPos.ToFieldPos() + " schlägt gegnerische Figur auf " + newPos.ToFieldPos();
                            case 3:
                                return Figur.Klasse.ToString() + " " + Figur.Color.ToString() + " auf " + oldPos.ToFieldPos() + " schlägt gegnerische Figur und zieht auf " + newPos.ToFieldPos();
                            case 4:
                                return (newPos.x > oldPos.x ? "Kleine Rochade" : "Große Rochade") + " auf Linie " + newPos.ToFieldPos()[1];
                            default:
                                return "Fehler: unmöglicher Zugtyp!";
                        }
                    else return "Root-Zustand";

                }
            }
            [Serializable]
            public class State
            {
                public int[][] Feld = new int[8][];
                public float Value;
                public State Parent;
                public int Level;
                public List<State> Childs = new List<State>();
                public Schachzug Zug;
                public List<PFigur> wFiguren = new List<PFigur>(), sFiguren = new List<PFigur>();
                public int playerID;
                public bool isFinal;
                public bool isFinalState()
                {
                    if (wFiguren.Exists(x => x.Klasse == PFigur.Typ.König) && sFiguren.Exists(x => x.Klasse == PFigur.Typ.König))
                        return false;
                    else
                        return true;
                }
                public State(Schachzug zug, State parent, int[][] prevField, int pid)
                {
                    playerID = pid;
                    Zug = zug;
                    Parent = parent;
                    foreach (PFigur figur in parent.wFiguren)
                        wFiguren.Add(new PFigur(figur.Klasse, figur.position, figur.Color));
                    foreach (PFigur figur in parent.sFiguren)
                        sFiguren.Add(new PFigur(figur.Klasse, figur.position, figur.Color));
                    List<PFigur>[] Sets = new List<PFigur>[] { wFiguren, sFiguren };
                    PFigur Figur = Sets[pid].Find(x => zug.Figur.position == x.position);
                    for (int i = 0; i < prevField.Length; i++)
                    {
                        Feld[i] = new int[8];
                        prevField[i].CopyTo(Feld[i], 0);
                    }
                    int tEnemy;
                    Feld[Figur.position.y][Figur.position.x] = 0;
                    Feld[zug.newPos.y][zug.newPos.x] = Figur.ToClassIndex();
                    Figur.FirstTurn = false;
                    if (zug.moveType == 4)
                    {
                        PFigur Tower = Sets[playerID].Find(Turm => Turm.position == new PFigur.Position((int)((zug.newPos.x - 2) * 7.0f / 4.0f), zug.newPos.y));
                        Tower.position.x = zug.newPos.x - (zug.newPos.x - 4) / 2;
                        Board.Schachfeld[zug.newPos.y][Tower.position.x] = Tower.ToClassIndex();
                        Tower.FirstTurn = false;
                    }
                    Figur.position = zug.newPos;
                    if (new int[] { 2, 3 }.Contains(zug.moveType))
                    {
                        tEnemy = Sets[1 - playerID].FindIndex(delegate (PFigur figur) { return figur.position.x == zug.newPos.x && figur.position.y == zug.newPos.y; });
                        Sets[1 - playerID].RemoveAt(tEnemy);
                    }
                    if (Figur.Klasse == PFigur.Typ.Bauer && zug.newPos.y == pid * 7)
                    {
                        Figur = new PFigur(PFigur.Typ.Dame, zug.newPos, Figur.Color);
                        Figur.FirstTurn = false;
                        Feld[zug.newPos.y][zug.newPos.x] = Figur.ToClassIndex();
                    }
                }
                public State(Schachzug zug, State parent, int pid, float value, int[][] refField, List<PFigur> wfiguren, List<PFigur> sfiguren)
                {
                    playerID = pid;
                    Zug = zug;
                    Parent = parent;
                    Value = value;
                    for (int i = 0; i < refField.Length; i++)
                    {
                        Feld[i] = new int[8];
                        refField[i].CopyTo(Feld[i], 0);
                    }
                    wFiguren.AddRange(wfiguren);
                    sFiguren.AddRange(sfiguren);
                }
            }
            public static void WriteTreeCount()
            {
                /*while (true)
                {
                    Console.SetCursorPosition(0, 0);
                    try
                    {
                        Console.Write(System.Diagnostics.Process.GetCurrentProcess().Threads.Count);
                    }
                    catch (Exception) { }
                }*/
            }
            public static List<PFigur> FigursAsITarget(PFigur Figur, int[][] cField, List<PFigur> dTargeting, List<PFigur> FigurSet)
            {
                List<PFigur> results = new List<PFigur>();
                for (int i = 0; i < dTargeting.Count; i++)
                {
                    List<PFigur> tResults = FigursAsTarget(dTargeting[i].position, FigurSet[i].Color, FigurSet);
                    List<PFigur.Typ> compClasses;
                    switch (Figur.Klasse)
                    {
                        case PFigur.Typ.König:
                        case PFigur.Typ.Dame:
                            compClasses = new List<PFigur.Typ>() { PFigur.Typ.Dame, PFigur.Typ.Turm, PFigur.Typ.Läufer };
                            break;
                        case PFigur.Typ.Bauer:
                        case PFigur.Typ.Läufer:
                            compClasses = new List<PFigur.Typ>() { PFigur.Typ.Dame, PFigur.Typ.Läufer };
                            break;
                        case PFigur.Typ.Turm:
                            compClasses = new List<PFigur.Typ>() { PFigur.Typ.Dame, PFigur.Typ.Turm };
                            break;
                        default:
                            compClasses = new List<PFigur.Typ>();
                            break;
                    }
                    List<PFigur> tResults2 = tResults.FindAll(x => compClasses.Contains(x.Klasse));
                    for (int j = 0; j < tResults2.Count; j++)
                    {
                        Vector2 dir1 = ((Vector2)Figur.position - dTargeting[i].position) / ((Vector2)Figur.position - dTargeting[i].position).magnitude;
                        Vector2 dir2 = ((Vector2)dTargeting[i].position - tResults[j].position) / ((Vector2)dTargeting[i].position - tResults[j].position).magnitude;
                        if (dir1 == dir2)
                            results.Add(tResults[j]);
                    }
                }
                return results;
            }
            public static List<PFigur> FigursAsTarget(PFigur.Position position, PFigur.Farbe farbe, List<PFigur> FigurSet)
            {
                return FigursAsTarget(position, farbe, FigurSet, false);
            }
            public static List<PFigur> FigursAsTarget(PFigur.Position position, PFigur.Farbe farbe, List<PFigur> FigurSet, bool include6)
            {
                List<PFigur> results = new List<PFigur>();
                for (int i = 0; i < FigurSet.Count; i++)
                {
                    if (FigurSet[0].Color != farbe)
                    {
                        if (new int[] { 2, 3 }.Contains(FigurSet[i].Movement[position.y, position.x]))
                            results.Add(FigurSet[i]);
                    }
                    else
                    {
                        if (FigurSet[i].Movement[position.y, position.x] == 5)
                            results.Add(FigurSet[i]);
                    }
                    if (include6 && FigurSet[i].Movement[position.y, position.x] == 6)
                        results.Add(FigurSet[i]);
                }
                return results;
            }
            public static List<PFigur> PositionAsTarget(PFigur.Position position, List<PFigur> FigurSet)
            {
                List<PFigur> results = new List<PFigur>();
                for (int i = 0; i < FigurSet.Count; i++)
                {
                    if (new int[] { 1, 2, 3, 4, 5 }.Contains(FigurSet[i].Movement[position.y, position.x])
                        && !(FigurSet[i].Klasse == PFigur.Typ.Bauer && FigurSet[i].Movement[position.y, position.x] == 1))
                        results.Add(FigurSet[i]);
                }
                return results;
            }
            public float RateState(State current, string debug)
            {
                float result = 0f, matValue = 0f, GuardValue = 0f, FigurValues = 0f, PChessValue = 0f /*!!*/, EChessValue = 0f, MovementValue = 0f;
                List<PFigur>[] FigurSets = new List<PFigur>[] { new List<PFigur>(), new List<PFigur>() };
                FigurSets[0].AddRange(current.wFiguren);
                FigurSets[1].AddRange(current.sFiguren);
                float[] FigurGuards = new float[FigurSets[Player.playerID].Count];
                float[] FigurThreats = new float[FigurSets[1 - Player.playerID].Count];
                bool hasThreats = false;
                PFigur PKing = FigurSets[Player.playerID].Find(x => x.Klasse == PFigur.Typ.König);
                PFigur EKing = FigurSets[1 - Player.playerID].Find(x => x.Klasse == PFigur.Typ.König);
                PKing.Movement = Spieler.OnSelect(PKing, current.Feld, true);
                EKing.Movement = Spieler.OnSelect(EKing, current.Feld, true);
                EKing.Movement = Spieler.checkForbidden(EKing, EKing.Movement, current.Feld, FigurSets[Player.playerID], FigurSets[1 - Player.playerID], true);
                for (int i = 0; i < FigurSets[Player.playerID].Count; i++)
                {
                    float tLostFigureValue = 0f, tWonFigureValue = 0f;
                    PFigur Figur = FigurSets[Player.playerID][i];
                    matValue += Figur.Punkte;
                    List<PFigur> guards = new List<PFigur>(), threats = new List<PFigur>();
                    guards = FigursAsTarget(Figur.position, PFigur.Farbe.schwarz, FigurSets[Player.playerID]);
                    threats = FigursAsTarget(Figur.position, PFigur.Farbe.schwarz, FigurSets[1 - Player.playerID]);
                    guards.AddRange(FigursAsITarget(Figur, current.Feld, guards, FigurSets[Player.playerID]));
                    threats.AddRange(FigursAsITarget(Figur, current.Feld, threats, FigurSets[1 - Player.playerID]));
                    FigurGuards[i] += (guards.Count + 1) - threats.Count;
                    if (threats.Count > 0) hasThreats = true;
                    // Ermittlung des Bewegungsfreiraums gekoppelt mit den Schutzwerten
                    GuardValue += guards.Count;
                    MovementValue += FigurSets[Player.playerID][i].Movement.CountValues(1, 4).Count;
                    if (guards.Exists(x => x.Klasse == PFigur.Typ.König)) // König sollte keine Figuren schützen da er das Hauptziel ist
                        GuardValue--;
                    if (Figur.Klasse == PFigur.Typ.Dame) // Schutz der Dame sinnlos, Dame wird fast immer geschlagen
                        GuardValue = 0;
                    // Ermittlung der theoretischen Bedrohungswerte bei Bedrohung eigener Figuren
                    if (FigurGuards[i] <= 0 && threats.Count > 0)
                    {
                        tLostFigureValue = Figur.Punkte;
                        tLostFigureValue += guards.Sum(x => x.Punkte);
                        /*
                         * Minimum-Methode der Figuren-Eliminierung
                         * Vorraussage der gegnerischen Schlagreihnfolge (schwächste opfern)
                         */
                        List<PFigur> values = new List<PFigur>();
                        values.AddRange(threats); // !!!!!!!
                        for (int g = 0; g < guards.Count; g++)
                        {
                            tWonFigureValue += values.Min(x => x.Punkte);
                            values.Remove(values.Find(x => x.Punkte == values.Min(y => y.Punkte)));
                        }
                        /*
                         * AVG-Methode der Figuren-Eliminierung
                         * Standardisierung der Gegnerischen Schlagreihnfolge
                         */
                        /*List<int> values = FigurValuesAsTarget(Figur, FigurSets[Player.playerID]);
                        for (int g = 0; g < guardians; g++)
                        {
                            tWonFigureValue += (float)values.Average();
                        }*/
                        FigurValues += Clamp(float.NegativeInfinity, 1, tWonFigureValue - tLostFigureValue);
                    }
                    else if (FigurGuards[i] > 0 && threats.Count > 0)
                    {
                        tLostFigureValue = Figur.Punkte;
                        tWonFigureValue = threats.Min(x => x.Punkte);//threats.Sum(x => x.Punkte);
                        List<PFigur> values = new List<PFigur>();
                        values.AddRange(guards);
                        for (int g = 0; g < threats.Count - 1; g++)
                        {
                            tLostFigureValue += values.Min(x => x.Punkte);
                            values.Remove(values.Find(x => x.Punkte == values.Min(y => y.Punkte)));
                        }
                        FigurValues += Clamp(float.NegativeInfinity, 0, tWonFigureValue - tLostFigureValue);
                    }
                }
                // Ermittlung der Bedrohungswerte ohne Bedorhung eigener Figuren
                if (!hasThreats)
                {
                    for (int i = 0; i < FigurSets[Player.playerID].Count; i++)
                    {
                        List<PFigur.Position> targetPositions = FigurSets[Player.playerID][i].Movement.CountValues(2, 3);
                        for (int j = 0; j < targetPositions.Count; j++)
                        {
                            List<PFigur> oAttacks = FigursAsTarget(targetPositions[j], PFigur.Farbe.weiß, FigurSets[Player.playerID]);
                            List<PFigur> tGuards = FigursAsTarget(targetPositions[j], PFigur.Farbe.weiß, FigurSets[1 - Player.playerID]);
                            oAttacks.AddRange(FigursAsITarget(FigurSets[1 - Player.playerID].Find(x => x.position == targetPositions[j]), current.Feld, oAttacks, FigurSets[Player.playerID]));
                            tGuards.AddRange(FigursAsITarget(FigurSets[1 - Player.playerID].Find(x => x.position == targetPositions[j]), current.Feld, tGuards, FigurSets[1 - Player.playerID]));
                            if (oAttacks.Count == 0) continue;
                            if (oAttacks.Count > tGuards.Count)
                            {
                                int value = tGuards.Sum(x => x.Punkte);
                                for (int k = 0; k < tGuards.Count; k++)
                                {
                                    value -= oAttacks.Min(x => x.Punkte);
                                    oAttacks.Remove(oAttacks.Find(x => x.Punkte == oAttacks.Min(y => y.Punkte)));
                                }
                                FigurValues += Clamp(1, float.PositiveInfinity, value);
                            }
                            else
                            {
                                int value = -oAttacks.Sum(x => x.Punkte);
                                for (int k = 0; k < oAttacks.Count; k++)
                                {
                                    value += tGuards.Min(x => x.Punkte);
                                    oAttacks.Remove(tGuards.Find(x => x.Punkte == tGuards.Min(y => y.Punkte)));
                                }
                                FigurValues += Clamp(1, float.PositiveInfinity, value);
                            }
                        }
                    }
                }
                // 1. Zug Gegner -> Bedrohung für eigene Figur prüfen. Wenn starker Schutz -> Angriff. Schutz EIGENER Figur.
                PFigur.movement NTFields = new int[3][] {
                    new int[3] { 0, 0, 0},
                    new int[3] { 0, 0, 0},
                    new int[3] { 0, 0, 0}};
                List<PFigur.Position> EKingMov = EKing.Movement.CountValues(1, 4);
                for (int i = 0; i < FigurSets[1 - Player.playerID].Count; i++)
                {
                    PFigur Figur = FigurSets[1 - Player.playerID][i];
                    matValue -= Figur.Punkte;
                    for (int j = 0; j < EKingMov.Count; j++)
                    {
                        int x = (EKingMov[j].x - EKing.position.x) + 1, y = (EKingMov[j].y - EKing.position.y) + 1;
                        NTFields[y, x] = (int)Clamp(0, 2, PositionAsTarget(EKingMov[j], FigurSets[Player.playerID]).Count - (PositionAsTarget(EKingMov[j], FigurSets[1 - Player.playerID]).Count - 2));
                    }
                }
                // Bedrohungssituation der Könige Analysieren
                List<PFigur.Position> EChess = EKing.Movement.CountValues(1, 4);
                EChessValue = EChess.Count;
                float DoChess = 0f;
                for (int i = 0; i < EKingMov.Count; i++)
                {
                    int x = (EKingMov[i].x - EKing.position.x) + 1, y = (EKingMov[i].y - EKing.position.y) + 1;
                    if (NTFields[y, x] == 0)
                    {
                        DoChess = 0f;
                        break;
                    }
                    else
                    {
                        DoChess += NTFields[y, x];
                    }
                }
                /* Erzwinge Bewegung in Richtung Rand mittels Schach, 
                 * Minimierung relevanter gegnerischer Bewegungen, +
                 * Minimierung aller Bewegungen des Gegners, +
                 * Maximierung eigener, 
                 * Distanzieren des Königs von eigenen Figuren und Figurenbewegungen +
                 * Befreiung eigener Figuren + Schutzwert
                 */
                // Schach = Schachmatt bevorzugen
                // Bedrohung der 5en des Königs -> Überlegenheit
                // Bedrohung durch 2+ Figuren!
                // mehrere Schutzziele vermeiden -> Ablenkung
                if (Spieler.inCheck(FigurSets[Player.playerID], FigurSets[1 - Player.playerID]))
                    result = float.NegativeInfinity;
                else
                    result = (matValue * 100 + (FigurValues - Math.Abs(FigurValues) / 1.25f) * 50) + DoChess * (Spieler.inCheck(FigurSets[1 - Player.playerID], FigurSets[Player.playerID]) ? 20 : 10) + MovementValue;
                //Debug.Log(current.Zug + "|" + EKing.Movement.CountValues(6, 6).Count);
                current.Value = result;
                // Gesamter Materialwert des Spielfeldes (+Eigene -Gegner) + Schutz der eigenen Figuren (+Punkte bei Schutz, -Punkte bei Bedrohung, Sonst 0), - Schutz der gegnerischen Figuren
                return result;
            }
            public static List<PFigur> getFiguresFromBoard(int[][] Board, PFigur.Farbe color)
            {
                List<PFigur> results = new List<PFigur>();
                for (int i = 0; i < Board.Length; i++)
                {
                    for (int j = 0; j < Board[i].Length; j++)
                    {
                        if (Board[i][j] != 0 && (Board[i][j] - 1) - (int)color * 9 < 6 && (Board[i][j] - 1) - (int)color * 9 >= 0)
                        {
                            results.Add(new PFigur((PFigur.Typ)((Board[i][j] - 1) - (int)color * 9), new PFigur.Position(j, i), color));
                        }
                    }
                }
                return results;
            }
            public void MakeTurn()
            {
                StateTree.Clear();
                int maxdepth = 1;
                for (int i = 0; i < Board.Schachfeld.Length; i++)
                {
                    Root[i] = new int[8];
                    Board.Schachfeld[i].CopyTo(Root[i], 0);
                }
                Thread th3 = new Thread(new ParameterizedThreadStart(FillStateTree));
                th3.Start(new object[] { Player.playerID, maxdepth, new State(null, null, Player.playerID, 0f, Root, Board.wFiguren, Board.sFiguren) });
                while (th3.IsAlive) { }
                List<Thread> tasks = new List<Thread>();
                List<State> states = new List<State>();
                for (int i = 0; i < StateTree.Count; i++)
                {
                    for (int j = 0; j < StateTree[i].Count; j++)
                    {
                        states.Add(StateTree[i][j]);
                    }
                }
                for (int i = 0; i < states.Count; i++)
                {
                    State state = states[i];
                    for (int j = 0; j < state.wFiguren.Count; j++)
                        state.wFiguren[j].Movement = Spieler.OnSelect(state.wFiguren[j], state.Feld, true);
                    for (int j = 0; j < state.sFiguren.Count; j++)
                        state.sFiguren[j].Movement = Spieler.OnSelect(state.sFiguren[j], state.Feld, true);
                    if (state.Zug != null)
                        states[i].Value = RateState(state, state.Zug == null ? "Root-Zustand" : state.Zug.ToString());
                }
                /*
                 *      Kurzsichtiges Verfahren:
                 */
                float maxValue;
                Schachzug Selected = null;
                maxValue = StateTree[1][0].Value;
                Selected = StateTree[1][0].Zug;
                for (int i = 0; i < StateTree[1].Count; i++)
                {
                    if (maxValue < StateTree[1][i].Value)
                    {
                        maxValue = StateTree[1][i].Value;
                        Selected = StateTree[1][i].Zug;
                    }
                }
                /*
                 * Anwendung des Zugs
                 */
                if (maxValue == float.NegativeInfinity)
                {
                    // Schachmatt!
                }
                else
                {
                    Board.P1.ClearSelection();
                    PFigur.MakeTurn(Selected, 1);
                }
                /*
                 * Bewertungsfunktion:
                 *  - Figuren auf dem Feld
                 *  - Gegnerische Figuren bedroht
                 *  - Eigene Figuren bedroht
                 *  - Sicherung gegnerischer Figuren
                 *  - Sicherung eigener Figuren
                 *  - Begehbare Felder
                 */
            }
            public KI(Spieler player)
            {
                Player = player;
                Root = new int[8][] {
                new int[8] { 0xB, 0xC, 0xD, 0xE, 0xF, 0xD, 0xC, 0xB },
                new int[8] { 0xA, 0xA, 0xA, 0xA, 0xA, 0xA, 0xA, 0xA },
                new int[8] { 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0 },
                new int[8] { 0x0, 0x0, 0x0, 0x6, 0x0, 0x0, 0x0, 0x0 },
                new int[8] { 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0 },
                new int[8] { 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0 },
                new int[8] { 0x1, 0x1, 0x1, 0x1, 0x1, 0x1, 0x1, 0x1 },
                new int[8] { 0x2, 0x3, 0x4, 0x5, 0x6, 0x4, 0x3, 0x2 }
                };
                for (int i = 0; i < Board.Schachfeld.Length; i++)
                {
                    Root[i] = new int[8];
                    Board.Schachfeld[i].CopyTo(Root[i], 0);
                }
                switch (Player.KITyp)
                {
                    case Spieler.KIType.DynamicLearning:
                        break;
                    case Spieler.KIType.HardSearch:
                        Thread th = new Thread(new ParameterizedThreadStart(FillStateTree));
                        FillStateTree(Player.playerID);
                        Thread th2 = new Thread(WriteTreeCount);
                        th.Start();
                        th2.Start();
                        break;
                    case Spieler.KIType.StaticLearning:
                        break;
                }
            }
        }
        [Serializable]
        public class PFigur
        {
            public Transform startTransform;
            public static void MakeTurn(KI.Schachzug zug, int playerID)
            {
                Board.P1.ClearSelection();
                zug.Figur.FirstTurn = false;
                Board.Schachfeld[zug.Figur.position.y][zug.Figur.position.x] = 0x0;
                Board.Schachfeld[zug.newPos.y][zug.newPos.x] = zug.Figur.ToClassIndex();
                List<PFigur>[] Sets = new List<PFigur>[] { Board.wFiguren, Board.sFiguren };
                Zugverlauf.Add(new KI.Schachzug(zug.Figur, new Position(zug.oldPos.x, zug.oldPos.y), zug.newPos, zug.moveType));
                scrollPos = new Vector2(scrollPos.x, Zugverlauf.Count * 22);
                if (zug.moveType == 4)
                {
                    PFigur Tower = Sets[playerID].Find(Turm => Turm.position == new Position((int)((zug.newPos.x - 2) * 7.0f / 4.0f), zug.newPos.y));
                    Board.Schachfeld[zug.newPos.y][Tower.position.x] = 0;
                    Tower.position.x = zug.newPos.x - (zug.newPos.x - 4) / 2;
                    Board.Schachfeld[zug.newPos.y][Tower.position.x] = Tower.ToClassIndex();
                    Tower.FirstTurn = false;
                    instance.StartCoroutine(AnimateMovement(Tower.Objekt, new Vector3(0, 0, (Tower.position.x - (int)((zug.newPos.x - 2) * 7.0f / 4.0f)) * -11.25f), zug, playerID));
                }
                Vector2 idiff = (Vector2)zug.newPos - zug.oldPos;
                zug.Figur.position.x = zug.newPos.x;
                zug.Figur.position.y = zug.newPos.y;
                if (new int[] { 2, 3 }.Contains(zug.moveType))
                {
                    int index = Sets[1 - playerID].FindIndex(delegate (PFigur figur) { return (figur.position.x == zug.newPos.x && figur.position.y == zug.newPos.y + zug.moveType - 2); });
                    Physics.IgnoreCollision(zug.Figur.Objekt.GetComponent<Collider>(), Sets[1 - playerID][index].Objekt.GetComponent<Collider>(), false);
                    zug.Figur.Objekt.GetComponent<Rigidbody>().isKinematic = true;
                    //Board.P1.Punkte += Board.sFiguren[index].Punkte;
                    Sets[1 - playerID].RemoveAt(index);
                    zug.Figur.Objekt.tag = "MadeTurn";
                }
                instance.StartCoroutine(AnimateMovement(zug.Figur.Objekt, new Vector3(idiff.y * -11.25f, 0, idiff.x * -11.25f), zug, playerID));
                if (zug.Figur.Color == Farbe.weiß && zug.Figur.Klasse == Typ.Bauer && zug.newPos.y == 0)
                {
                    instance.CameraTransform.enabled = true;
                    instance.CameraTransform.GetComponent<AudioListener>().enabled = true;
                    instance.CameraMain.enabled = false;
                    instance.CameraMain.GetComponent<AudioListener>().enabled = false;
                    toTransform = zug.Figur;
                }
                else
                    instance.SwitchTurns();
            }
            public static IEnumerator AnimateMovement(GameObject Objekt, Vector3 diff, KI.Schachzug zug, int playerID)
            {
                animates = true;
                diff.y = 0;
                for (int i = 0; i < 20; i++)
                {
                    Objekt.transform.localPosition += diff / 20;
                    yield return new WaitForSeconds(0);
                }
                animates = false;
                if (zug.Figur.Color == Farbe.schwarz && zug.Figur.Klasse == Typ.Bauer && zug.newPos.y == 7 * playerID)
                {
                    string oldTag = zug.Figur.Objekt.tag;
                    Destroy(zug.Figur.Objekt);
                    int index = Board.sFiguren.FindIndex(x => x.position == zug.Figur.position);
                    Board.sFiguren[index] = new PFigur(Typ.Dame, zug.newPos, Board.sFiguren[index].Color);
                    Board.sFiguren[index].Objekt = Instantiate(GameObject.Find("Black/Königin"));
                    Board.sFiguren[index].Objekt.transform.parent = GameObject.Find("Black").transform;
                    Board.sFiguren[index].Objekt.GetComponent<Rigidbody>().isKinematic = true;
                    Board.sFiguren[index].Objekt.transform.localPosition = new Vector3(Board.sFiguren[index].position.y * -11.25f, -24.50f, Board.sFiguren[index].position.x * -11.25f);
                    Board.sFiguren[index].Objekt.layer = LayerMask.NameToLayer("FigurS");
                    Board.sFiguren[index].Objekt.tag = oldTag;
                    Board.Schachfeld[zug.newPos.y][zug.newPos.x] = Board.sFiguren[index].ToClassIndex();
                    foreach (PFigur figur in Board.sFiguren)
                    {
                        Debug.Log(figur.Klasse + figur.position.ToFieldPos());
                    }
                    Board.sFiguren[index].Objekt.GetComponent<Rigidbody>().isKinematic = false;
                }
                yield return null;
            }
            public bool FirstTurn;
            public GameObject Objekt
            {
                get { return _Objekt; }
                set
                {
                    startTransform = value.transform;
                    _Objekt = value;
                }
            }
            public GameObject _Objekt;
            public readonly int Punkte;
            public enum Farbe
            {
                weiß,
                schwarz
            }
            public Farbe Color;
            public enum Typ
            {
                Bauer,
                Turm,
                Springer,
                Läufer,
                König,
                Dame
            }
            public readonly Typ Klasse;
            public class Position
            {
                public int x;
                public int y;
                public static Position operator +(Position A, int[] B)
                {
                    Position newPos;
                    if (B.Length == 2)
                        newPos = new Position(A.x + B[1], A.y + B[0]);
                    else throw new ArgumentOutOfRangeException();
                    return newPos;
                }
                public static Position operator +(Position A, Position B)
                {
                    return A + new int[] { B.y, B.x };
                }
                public static bool operator ==(Position a, Position b)
                {
                    return a.x == b.x && a.y == b.y;
                }
                public static bool operator !=(Position a, Position b)
                {
                    return !(a == b);
                }
                public static implicit operator Vector2(Position a)
                {
                    return new Vector2(a.x, a.y);
                }
                public static implicit operator Position(Vector2 a)
                {
                    return new Position((int)a.x, (int)a.y);
                }
                public string ToFieldPos()
                {
                    return Convert.ToChar(x + 65).ToString() + (y + 1).ToString();
                }
                public Position(int X, int Y)
                {
                    x = X;
                    y = Y;
                }
            }
            public Position position;
            public class movement
            {
                public int[][] Field;
                public List<PFigur.Position> CountValues(int ValueMin, int ValueMax)
                {
                    List<PFigur.Position> temp = new List<PFigur.Position>();
                    for (int i = 0; i < 8; i++)
                    {
                        for (int j = 0; j < 8; j++)
                        {
                            if (Field[i][j] >= ValueMin && Field[i][j] <= ValueMin)
                            {
                                temp.Add(new PFigur.Position(j, i));
                            }
                        }
                    }
                    return temp;
                }
                public static implicit operator movement(int[][] field)
                {
                    movement temp = new movement();
                    temp.Field = field;
                    return temp;
                }
                public static implicit operator int[][] (movement mov)
                {
                    return mov.Field;
                }
                public int this[int i, int j]
                {
                    get { return this.Field[i][j]; }
                    set { this.Field[i][j] = (int)value; }
                }
            }
            public movement Movement;
            public int ToClassIndex()
            {
                if (Color == Farbe.weiß)
                {
                    switch (Klasse)
                    {
                        case Typ.Bauer:
                            return 0x1;
                        case Typ.Dame:
                            return 0x5;
                        case Typ.König:
                            return 0x6;
                        case Typ.Läufer:
                            return 0x4;
                        case Typ.Springer:
                            return 0x3;
                        case Typ.Turm:
                            return 0x2;
                        default:
                            return 0x0;
                    }
                }
                else
                {
                    switch (Klasse)
                    {
                        case Typ.Bauer:
                            return 0xA;
                        case Typ.Dame:
                            return 0xE;
                        case Typ.König:
                            return 0xF;
                        case Typ.Läufer:
                            return 0xD;
                        case Typ.Springer:
                            return 0xC;
                        case Typ.Turm:
                            return 0xB;
                        default:
                            return 0x0;
                    }
                }
            }
            /* INT-Werte für Movement Array:
            0 - Keine Bewegung
            1 - normale Bewegung
            2 - Schlagen einer Figur
            3 - Beiläufiges Schlagen einer Figur 
            4 - Rochade
            */
            public PFigur(Typ typ, Position pos, Farbe color)
            {
                Color = color;
                Movement = new int[8][] {
                new int[8] { 0, 0, 0, 0, 0, 0, 0, 0 },
                new int[8] { 0, 0, 0, 0, 0, 0, 0, 0 },
                new int[8] { 0, 0, 0, 0, 0, 0, 0, 0 },
                new int[8] { 0, 0, 0, 0, 0, 0, 0, 0 },
                new int[8] { 0, 0, 0, 0, 0, 0, 0, 0 },
                new int[8] { 0, 0, 0, 0, 0, 0, 0, 0 },
                new int[8] { 0, 0, 0, 0, 0, 0, 0, 0 },
                new int[8] { 0, 0, 0, 0, 0, 0, 0, 0 }
                };
                FirstTurn = true;
                Klasse = typ;
                position = pos;
                switch (typ)
                {
                    case Typ.Bauer:
                        Punkte = 1;
                        break;
                    case Typ.Turm:
                        Punkte = 5;
                        break;
                    case Typ.Springer:
                        Punkte = 3;
                        break;
                    case Typ.Läufer:
                        Punkte = 3;
                        break;
                    case Typ.Dame:
                        Punkte = 9;
                        break;
                    case Typ.König:
                        Punkte = 100;
                        break;
                    default:
                        throw new System.Exception("Der falsche Typ wurde eingegeben.");
                }
            }
        }
        public class Spieler
        {
            /* Definition der INT-Werte der Figuren: Hexadezimal
            Weiß:
                Bauer = 0x1
                Turm = 0x2
                Springer = 0x3
                Läufer = 0x4
                Königin = 0x5
                König = 0x6
            Schwarz:
                Bauer = 0xA
                Turm = 0xB
                Springer = 0xC
                Läufer = 0xD
                Königin = 0xE
                König = 0xF
            */
            public int playerID;
            public string Name;
            public KIType KITyp;
            public KI assignedKI;
            public int Punkte;
            private int selected;
            PFigur.Farbe Farbe;
            public int Selected
            {
                get { return selected; }
                set
                {
                    selected = value;
                    List<PFigur> FigurSet;
                    if (playerID == 0)
                        FigurSet = Board.wFiguren;
                    else
                        FigurSet = Board.sFiguren;
                    if (value >= 0 && value < FigurSet.Count)
                    {
                        if (playerID == 0 && FigurSet[value].Klasse == PFigur.Typ.König)
                        {
                            for (int i = 0; i < Board.sFiguren.Count; i++)
                            {
                                Board.sFiguren[i].Movement = OnSelect(Board.sFiguren[i], Board.Schachfeld, new List<int> { 0, 80, 0x6 }, false);
                            }
                        }
                        FigurSet[value].Movement = OnSelect(FigurSet[value], Board.Schachfeld, false);
                        if (FigurSet[value].Color == PFigur.Farbe.weiß)
                        {
                            List<PFigur.Position> Positions = FigurSet[value].Movement.CountValues(1, 1);
                            for (int i = 0; i < Positions.Count; i++)
                            {
                                Free[i].transform.localPosition = new Vector3((7 - Positions[i].y) * 11.25f, -24.96f, Positions[i].x * -11.25f);
                            }
                            Positions.Clear();
                            Positions = FigurSet[value].Movement.CountValues(2, 2);
                            for (int i = 0; i < Positions.Count; i++)
                            {
                                Enemies[i].transform.localPosition = new Vector3((7 - Positions[i].y) * 11.25f, -24.96f, Positions[i].x * -11.25f);
                                int enemy = Board.sFiguren.FindIndex(delegate (PFigur figur) { return figur.position.x == Positions[i].x && figur.position.y == Positions[i].y; });
                                Board.sFiguren[enemy].Objekt.tag = "EnemyF";
                                if (Board.sFiguren[enemy].Objekt.GetComponent<MeshRenderer>() != null)
                                    Board.sFiguren[enemy].Objekt.GetComponent<MeshRenderer>().material = EnemyMat;
                                else
                                {
                                    for (int j = 0; j < Board.sFiguren[enemy].Objekt.GetComponentsInChildren<MeshRenderer>().Length; j++)
                                    {
                                        Board.sFiguren[enemy].Objekt.GetComponentsInChildren<MeshRenderer>()[j].materials = new Material[2] { EnemyMat, EnemyMat };
                                    }
                                }
                            }
                            Positions.Clear();
                            Positions = FigurSet[value].Movement.CountValues(3, 3);
                            for (int i = 0; i < Positions.Count; i++)
                            {
                                SEnemy[i].transform.localPosition = new Vector3((7 - Positions[i].y) * 11.25f, -24.96f, Positions[i].x * -11.25f);
                            }
                            Positions.Clear();
                            Positions = FigurSet[value].Movement.CountValues(4, 4);
                            for (int i = 0; i < Positions.Count; i++)
                            {
                                Spezial[i].transform.localPosition = new Vector3((7 - Positions[i].y) * 11.25f, -24.96f, Positions[i].x * -11.25f);
                            }
                        }
                    }
                }
            }
            public bool Schach;
            public enum KIType
            {
                None,
                HardSearch,
                StaticLearning,
                DynamicLearning
            }
            public static bool inCheck(List<PFigur> Set, List<PFigur> Set2)
            {
                PFigur König = Set.Find(x => x.Klasse == PFigur.Typ.König);
                for (int i = 0; i < Set2.Count; i++)
                {
                    if (!new int[] { 0, 1, 4 }.Contains(Set2[i].Movement[König.position.y, König.position.x]))
                        return true;
                }
                return false;
            }
            public bool activeTurn = true;
            public int[][] Schachfeld = new int[8][];
            public void ClearSelection()
            {
                if (selected >= 0)
                {
                    for (int i = 0; i < Board.wFiguren[Board.P1.Selected].Objekt.GetComponentsInChildren<MeshRenderer>().Length; i++)
                    {
                        Board.wFiguren[Board.P1.Selected].Objekt.GetComponentsInChildren<MeshRenderer>()[i].materials = new Material[2] { WeißMat, WeißMat };
                    }
                }
                GameObject[] temp = GameObject.FindGameObjectsWithTag("Free");
                for (int i = 0; i < temp.Length; i++)
                {
                    temp[i].transform.localPosition = new Vector3(0, -900, 0);
                }
                temp = GameObject.FindGameObjectsWithTag("Enemy");
                for (int i = 0; i < temp.Length; i++)
                {
                    temp[i].transform.localPosition = new Vector3(0, -900, 0);
                }
                temp = GameObject.FindGameObjectsWithTag("SEnemy");
                for (int i = 0; i < temp.Length; i++)
                {
                    temp[i].transform.localPosition = new Vector3(0, -900, 0);
                }
                temp = GameObject.FindGameObjectsWithTag("Spezial");
                for (int i = 0; i < temp.Length; i++)
                {
                    temp[i].transform.localPosition = new Vector3(0, -900, 0);
                }
                temp = GameObject.FindGameObjectsWithTag("Schach");
                for (int i = 0; i < temp.Length; i++)
                {
                    temp[i].transform.localPosition = new Vector3(0, -900, 0);
                }
                temp = GameObject.FindGameObjectsWithTag("EnemyF");
                for (int i = 0; i < temp.Length; i++)
                {
                    temp[i].tag = "Untagged";
                    if (temp[i].GetComponent<MeshRenderer>() != null)
                        temp[i].GetComponent<MeshRenderer>().material = SchwarzMat;
                    else
                    {
                        for (int j = 0; j < temp[i].GetComponentsInChildren<MeshRenderer>().Length; j++)
                        {
                            temp[i].GetComponentsInChildren<MeshRenderer>()[j].materials = new Material[2] { SchwarzMat, SchwarzMat };
                        }
                    }
                }
                GameObject.Find("Selection").transform.localPosition = new Vector3(0, -900, 0);
                for (int i = 0; i < Board.wFiguren.Count; i++)
                {
                    for (int j = 0; j < Board.sFiguren.Count; j++)
                    {
                        Physics.IgnoreCollision(Board.wFiguren[i].Objekt.GetComponent<Collider>(), Board.sFiguren[j].Objekt.GetComponent<Collider>());
                    }
                }
                Selected = -1;
            }
            public static PFigur.movement OnSelect(PFigur Figur, int[][] oFeld, bool isVirtual)
            {
                return OnSelect(Figur, oFeld, new List<int> { 0, 80 }, isVirtual);
            }
            public static PFigur.movement OnSelect(PFigur Figur, int[][] oFeld, List<int> ignoreIndex, bool isVirtual)
            {
                int[][] Feld = new int[9][];
                for (int i = 0; i < 8; i++)
                {
                    Feld[i] = new int[9];
                    oFeld[i].CopyTo(Feld[i], 0);
                    Feld[i][8] = 80;
                }
                Feld[8] = new int[9] { 80, 80, 80, 80, 80, 80, 80, 80, 80 };
                PFigur.movement movement = new int[][] {
                    new int[8] { 0, 0, 0, 0, 0, 0, 0, 0 },
                    new int[8] { 0, 0, 0, 0, 0, 0, 0, 0 },
                    new int[8] { 0, 0, 0, 0, 0, 0, 0, 0 },
                    new int[8] { 0, 0, 0, 0, 0, 0, 0, 0 },
                    new int[8] { 0, 0, 0, 0, 0, 0, 0, 0 },
                    new int[8] { 0, 0, 0, 0, 0, 0, 0, 0 },
                    new int[8] { 0, 0, 0, 0, 0, 0, 0, 0 },
                    new int[8] { 0, 0, 0, 0, 0, 0, 0, 0 }
                };
                int Faktor = 0;
                int[] Enemy = new int[2] { 0x0, 0x0 };
                if (Figur.Color == PFigur.Farbe.weiß)
                {
                    Faktor = 1;
                    Enemy = new int[] { 0xA, 0xF };
                }
                else
                {
                    Faktor = -1;
                    Enemy = new int[] { 0x1, 0x6 };
                }
                if (Figur != null)
                {
                    Action<PFigur.Position, PFigur.Position, bool> Felder = null;
                    Felder = (Richtung, Position, Rekursiv) =>
                    {
                        PFigur.Position newField = Position + Richtung;
                        if (newField.x >= 0 && newField.x < 8 && newField.y >= 0 && newField.y < 8)
                            if (ignoreIndex.Contains(Feld[newField.y][newField.x]))
                            {
                                movement[newField.y, newField.x] = 1;
                                if (Rekursiv)
                                    Felder(Richtung, newField, true);
                            }
                            else if (Feld[newField.y][newField.x] >= Enemy[0] && Feld[newField.y][newField.x] <= Enemy[1])
                            {
                                movement[newField.y, newField.x] = 2;
                            }
                            else
                            {
                                movement[newField.y, newField.x] = 5;
                            }
                    };
                    switch (Figur.Klasse)
                    {
                        case PFigur.Typ.Bauer:
                            for (int i = Figur.position.y - Faktor; i >= 0 && i < 8 && Math.Abs(i - Figur.position.y) <= (Figur.FirstTurn ? 2 : 1); i -= Faktor)
                            {
                                if (Feld[i][Figur.position.x] == 0)
                                {
                                    movement[i, Figur.position.x] = 1;
                                }
                                else break;
                            }
                            for (int i = 1, j = 0; j < 2; i *= -1, j++)
                            {
                                int val = Figur.position.y - Faktor >= 0 && Figur.position.y - Faktor < 8 &&
                                    Figur.position.x + i >= 0 && Figur.position.x + i < 8 ? Feld[Figur.position.y - Faktor][Figur.position.x + i] : 0;
                                if (val != 0)
                                    movement[Figur.position.y - Faktor, Figur.position.x + i] = val >= Enemy[0] && val <= Enemy[1] ? 2 : val != 0 ? 5 : 0;
                            }
                            if (Zugverlauf.Count > 0 && Zugverlauf[Zugverlauf.Count - 1].Figur.Klasse == PFigur.Typ.Bauer
                                && Math.Abs(Zugverlauf[Zugverlauf.Count - 1].newPos.y - Zugverlauf[Zugverlauf.Count - 1].oldPos.y) == 2
                                && Math.Abs(Figur.position.x - Zugverlauf[Zugverlauf.Count - 1].Figur.position.x) == 1
                                && Figur.position.y == Zugverlauf[Zugverlauf.Count - 1].Figur.position.y)
                            {
                                movement[Figur.position.y - Faktor, Zugverlauf[Zugverlauf.Count - 1].Figur.position.x] = 3;
                            }
                            break;
                        case PFigur.Typ.Dame:
                            for (int j = -1; j < 2; j += 2)
                                for (int i = 0; i < 4; i++)
                                {
                                    double a = 1 - Math.Pow(0, i), b = i - 2 < -1 ? 1 : i - 2;
                                    Felder(new PFigur.Position((int)a * j, (int)b * j), Figur.position, true);
                                }
                            break;
                        case PFigur.Typ.Läufer:
                            for (int i = 0; i < 4; i++)
                            {
                                int a = i / 2, b = i % 2;
                                Felder(new PFigur.Position(a == 0 ? -1 : a, b == 0 ? -1 : b), Figur.position, true);
                            }
                            break;
                        case PFigur.Typ.Turm:
                            for (int i = 0; i < 4; i++)
                            {
                                int a = (i / 2) * (int)Math.Pow(-1, i), b = (1 - (i / 2)) * (int)Math.Pow(-1, i);
                                Felder(new PFigur.Position((int)a, (int)b), Figur.position, true);
                            }
                            break;
                        case PFigur.Typ.König:
                            for (int j = -1; j < 2; j += 2)
                                for (int i = 0; i < 4; i++)
                                {
                                    double a = 1 - Math.Pow(0, i), b = i - 2 < -1 ? 1 : i - 2;
                                    Felder(new PFigur.Position((int)a * j, (int)b * j), Figur.position, false);
                                }
                            {

                            }
                            break;
                        case PFigur.Typ.Springer:
                            for (int j = 1; j < 3; j++)
                                for (int i = 0; i < 4; i++)
                                {
                                    int a = i / 2 == 0 ? -1 : i / 2, b = i % 2 == 0 ? -1 : i % 2;
                                    Felder(new PFigur.Position(a * j, b * (3 - j)), Figur.position, false);
                                }
                            break;
                    }
                }
                if (Figur.Color == PFigur.Farbe.weiß && !isVirtual)
                    movement = checkForbidden(Figur, movement, oFeld, Board.sFiguren, Board.wFiguren, false);
                return movement;
            }
            public static PFigur.movement checkForbidden(PFigur Figur, PFigur.movement movement, int[][] oFeld, List<PFigur> Pool, List<PFigur> Pool2, bool useFigur)
            {
                PFigur King;
                if (!useFigur)
                    King = Board.wFiguren.Find(x => x.Klasse == PFigur.Typ.König);
                else
                    King = Figur;
                List<PFigur> figurs = KI.FigursAsTarget(King.position, King.Color, Pool);
                Vector2 direction = new Vector2();
                int[][] totalMovement = new int[8][] { new int[8], new int[8], new int[8], new int[8], new int[8], new int[8], new int[8], new int[8] };
                for (int i = 0; i < totalMovement.Length; i++)
                {
                    for (int j = 0; j < totalMovement[i].Length; j++)
                    {
                        for (int k = 0; k < Pool.Count; k++)
                        {
                            if (Pool[k].Movement.Field[i][j] != 0 && Pool[k].Klasse != PFigur.Typ.Bauer)
                            {
                                totalMovement[i][j] = 1;
                                break;
                            }
                            else if (Pool[k].Klasse == PFigur.Typ.Bauer)
                            {
                                if (Pool[k].position.y < 7 && Pool[k].position.x < 7)
                                    totalMovement[Pool[k].position.y - 1 * (int)Math.Pow(-1, (float)Pool[k].Color)][Pool[k].position.x + 1] = 1;
                                if (Pool[k].position.y < 7 && Pool[k].position.x > 0)
                                    totalMovement[Pool[k].position.y - 1 * (int)Math.Pow(-1, (float)Pool[k].Color)][Pool[k].position.x - 1] = 1;
                            }
                        }
                    }
                }
                int iKing = 0x6 + (int)Figur.Color * 0x9, iTower = 0x2 + (int)Figur.Color * 0x9;
                //linke Rochade
                if (Figur.Klasse == PFigur.Typ.König && Figur.FirstTurn == true
                    && oFeld[7 - 7 * (int)Figur.Color].Reverse().Skip(3).Reverse().ToArray().SequenceEqual(new int[5] { iTower, 0, 0, 0, iKing })
                    && Pool2.Find(x => x.position == new PFigur.Position(0, 7 - 7 * (int)Figur.Color)).FirstTurn == true && totalMovement[7 - 7 * (int)Figur.Color].Reverse().Skip(3).Sum() == 0)
                {
                    movement[7 - 7 * (int)Figur.Color, Figur.position.x - 2] = 0x4;
                }
                //rechte Rochade
                if (Figur.Klasse == PFigur.Typ.König && Figur.FirstTurn == true
                    && oFeld[7 - 7 * (int)Figur.Color].Skip(4).ToArray().SequenceEqual(new int[4] { iKing, 0, 0, iTower })
                    && Pool2.Find(x => x.position == new PFigur.Position(7, 7 - 7 * (int)Figur.Color)).FirstTurn == true && totalMovement[7 - 7 * (int)Figur.Color].Skip(4).Sum() == 0)
                {
                    movement[7 - 7 * (int)Figur.Color, Figur.position.x + 2] = 0x4;
                }
                if (figurs.Count == 1)
                    switch (figurs[0].Klasse)
                    {
                        case PFigur.Typ.Dame:
                        case PFigur.Typ.Läufer:
                        case PFigur.Typ.Turm:
                            direction = ((Vector2)King.position - figurs[0].position) / ((Vector2)King.position - figurs[0].position).magnitude;
                            //new Vector2((King.position.x - figurs[0].position.x) == 0 ? 0 : (King.position.x - figurs[0].position.x) / Math.Abs((King.position.x - figurs[0].position.x)),
                            //(King.position.y - figurs[0].position.y) == 0 ? 0 : (King.position.y - figurs[0].position.y) / Math.Abs((King.position.x - figurs[0].position.x)));
                            break;
                        default:
                            direction = Vector2.zero;
                            break;
                    }
                // Möglichkeiten: Weg Blockieren, Flucht des Königs, Schlagen der Bedrohung. Rest verbieten
                for (int i = 0; i < movement.Field.Length; i++)
                {
                    for (int j = 0; j < movement.Field[i].Length; j++)
                    {
                        if (movement.Field[i][j] != 0)
                        {
                            //Flucht (durch Schlagen)
                            if (Figur.Klasse == PFigur.Typ.König)
                            {
                                //ACHTUNG: Bewegung: TK k->
                                if (totalMovement[i][j] == 1)
                                    movement.Field[i][j] = 6;
                            }
                            // Blockieren | Schlagen der Bedrohung
                            else if ((figurs.Count == 1 && (((new Vector2(j, i) - figurs[0].position) / (new Vector2(j, i) - figurs[0].position).magnitude != direction || direction == Vector2.zero) &&
                                !(j == figurs[0].position.x && i == figurs[0].position.y))) || figurs.Count > 1 || figurs.Count == 0)
                            {
                                if (figurs.Count == 0)
                                {
                                    foreach (PFigur figur in KI.FigursAsTarget(Figur.position, Figur.Color, Pool))
                                    {
                                        switch (figur.Klasse)
                                        {
                                            case PFigur.Typ.Dame:
                                            case PFigur.Typ.Läufer:
                                            case PFigur.Typ.Turm:
                                                /*
                                                Vector2 fdirection1 = new Vector2((Figur.position.x - figur.position.x) == 0 ? 0 : (Figur.position.x - figur.position.x) / Math.Abs((Figur.position.x - figur.position.x)),
                                                    (Figur.position.y - figur.position.y) == 0 ? 0 : (Figur.position.y - figur.position.y) / Math.Abs((Figur.position.x - figur.position.x)));
                                                Vector2 fdirection2 = new Vector2((King.position.x - figur.position.x) == 0 ? 0 : (King.position.x - figur.position.x) / Math.Abs((King.position.x - figur.position.x)),
                                                    (King.position.y - figur.position.y) == 0 ? 0 : (King.position.y - figur.position.y) / Math.Abs((King.position.x - figur.position.x)));
                                                Vector2 fdirection3 = new Vector2((j - figur.position.x) == 0 ? 0 : (j - figur.position.x) / Math.Abs((j - figur.position.x)),
                                                    (i - figur.position.y) == 0 ? 0 : (i - figur.position.y) / Math.Abs((j - figur.position.x)));*/
                                                Vector2 fdirection1 = ((Vector2)Figur.position - figur.position) / ((Vector2)Figur.position - figur.position).magnitude;
                                                Vector2 fdirection2 = ((Vector2)King.position - figur.position) / ((Vector2)King.position - figur.position).magnitude;
                                                Vector2 fdirection3 = (new Vector2(j, i) - figur.position) / (new Vector2(j, i) - figur.position).magnitude;
                                                if (fdirection1 == fdirection2 && fdirection1 == fdirection3 && fdirection2 == fdirection3)
                                                    movement.Field[i][j] = 6;
                                                break;
                                        }
                                    }
                                }
                                else
                                    movement.Field[i][j] = 6;
                            }
                        }
                    }
                }
                return movement;
            }
            public Spieler(int ID, KIType type, string pName, PFigur.Farbe color)
            {
                Name = pName;
                Punkte = 0;
                playerID = ID;
                KITyp = type;
                assignedKI = new KI(this);
                Farbe = color;
            }
        }
        [Serializable]
        public class Spielfeld
        {
            public Spieler P1, P2;
            public int activeTurn;
            public int[][] Schachfeld = new int[8][];
            public List<PFigur> wFiguren;
            public List<PFigur> sFiguren;
            public Spielfeld()
            {
                Schachfeld = new int[8][] {
                new int[8] { 0xB, 0xC, 0xD, 0xE, 0xF, 0xD, 0xC, 0xB },
                new int[8] { 0xA, 0xA, 0xA, 0xA, 0xA, 0xA, 0xA, 0xA },
                new int[8] { 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0 },
                new int[8] { 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0 },
                new int[8] { 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0 },
                new int[8] { 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0 },
                new int[8] { 0x1, 0x1, 0x1, 0x1, 0x1, 0x1, 0x1, 0x1 },
                new int[8] { 0x2, 0x3, 0x4, 0x5, 0x6, 0x4, 0x3, 0x2 }
                };
                wFiguren = new List<PFigur>();
                for (int i = 0; i < 8; i++)
                    wFiguren.Add(new PFigur(PFigur.Typ.Bauer, new PFigur.Position(i, 6), PFigur.Farbe.weiß));
                wFiguren.Add(new PFigur(PFigur.Typ.Turm, new PFigur.Position(0, 7), PFigur.Farbe.weiß));
                wFiguren.Add(new PFigur(PFigur.Typ.Springer, new PFigur.Position(1, 7), PFigur.Farbe.weiß));
                wFiguren.Add(new PFigur(PFigur.Typ.Läufer, new PFigur.Position(2, 7), PFigur.Farbe.weiß));
                wFiguren.Add(new PFigur(PFigur.Typ.König, new PFigur.Position(4, 7), PFigur.Farbe.weiß));
                wFiguren.Add(new PFigur(PFigur.Typ.Dame, new PFigur.Position(3, 7), PFigur.Farbe.weiß));
                wFiguren.Add(new PFigur(PFigur.Typ.Turm, new PFigur.Position(7, 7), PFigur.Farbe.weiß));
                wFiguren.Add(new PFigur(PFigur.Typ.Springer, new PFigur.Position(6, 7), PFigur.Farbe.weiß));
                wFiguren.Add(new PFigur(PFigur.Typ.Läufer, new PFigur.Position(5, 7), PFigur.Farbe.weiß));
                sFiguren = new List<PFigur>();
                for (int i = 0; i < 8; i++)
                    sFiguren.Add(new PFigur(PFigur.Typ.Bauer, new PFigur.Position(i, 1), PFigur.Farbe.schwarz));
                sFiguren.Add(new PFigur(PFigur.Typ.Turm, new PFigur.Position(0, 0), PFigur.Farbe.schwarz));
                sFiguren.Add(new PFigur(PFigur.Typ.Springer, new PFigur.Position(1, 0), PFigur.Farbe.schwarz));
                sFiguren.Add(new PFigur(PFigur.Typ.Läufer, new PFigur.Position(2, 0), PFigur.Farbe.schwarz));
                sFiguren.Add(new PFigur(PFigur.Typ.König, new PFigur.Position(4, 0), PFigur.Farbe.schwarz));
                sFiguren.Add(new PFigur(PFigur.Typ.Dame, new PFigur.Position(3, 0), PFigur.Farbe.schwarz));
                sFiguren.Add(new PFigur(PFigur.Typ.Turm, new PFigur.Position(7, 0), PFigur.Farbe.schwarz));
                sFiguren.Add(new PFigur(PFigur.Typ.Springer, new PFigur.Position(6, 0), PFigur.Farbe.schwarz));
                sFiguren.Add(new PFigur(PFigur.Typ.Läufer, new PFigur.Position(5, 0), PFigur.Farbe.schwarz));
            }
        }
        public static Spielfeld Board;
        public List<int> ClickableLayers;
        // Use this for initialization
        void Start()
        {
            SelectedMat = Resources.Load<Material>("Materialien/Selected");
            EnemyMat = Resources.Load<Material>("Materialien/Enemy");
            WeißMat = Resources.Load<Material>("Materialien/Weiß");
            SchwarzMat = Resources.Load<Material>("Materialien/Schwarz");
            HoverMat = Resources.Load<Material>("Materialien/Hover");
            CameraTransform.enabled = false;
            CameraTransform.GetComponent<AudioListener>().enabled = false;
            Board = new Spielfeld();
            Board.activeTurn = 0;
            Board.P1 = new Spieler(0, Spieler.KIType.None, "Player 1", PFigur.Farbe.weiß);
            Board.P2 = new Spieler(1, Spieler.KIType.StaticLearning, "Player 2", PFigur.Farbe.schwarz);
            ClickableLayers = new List<int>() { LayerMask.NameToLayer("Markierung"), LayerMask.NameToLayer("FigurW"), LayerMask.NameToLayer("FigurS") };
            List<Transform> temp = new List<Transform>();
            temp.AddRange(GameObject.Find("White").GetComponentsInChildren<Transform>());
            for (int i = 0; i < temp.Count; i++)
            {
                if (temp[i].parent != GameObject.Find("White").transform || temp[i].gameObject.layer != LayerMask.NameToLayer("FigurW"))
                {
                    temp.Remove(temp[i]);
                    i--;
                }
            }
            for (int i = 0; i < 16; i++)
            {
                Board.wFiguren[i].Objekt = temp[i].gameObject;
            }
            temp.Clear();
            temp.AddRange(GameObject.Find("Black").GetComponentsInChildren<Transform>());
            for (int i = 0; i < temp.Count; i++)
            {
                if (temp[i].parent != GameObject.Find("Black").transform)
                {
                    temp.Remove(temp[i]);
                    i--;
                }
            }
            for (int i = 0; i < 16; i++)
            {
                Board.sFiguren[i].Objekt = temp[i].gameObject;
            }
            for (int i = 0; i < 32; i++)
            {
                GameObject free = Instantiate(GameObject.Find("Free"));
                free.transform.parent = GameObject.Find("Clones").transform;
                free.tag = "Free";
                free.layer = LayerMask.NameToLayer("Markierung");
            }
            for (int i = 0; i < 8; i++)
            {
                GameObject chess = Instantiate(GameObject.Find("Schach"));
                chess.transform.parent = GameObject.Find("Clones").transform;
                chess.tag = "Schach";
                chess.layer = LayerMask.NameToLayer("Markierung");
            }
            for (int i = 0; i < 10; i++)
            {
                GameObject enemy = Instantiate(GameObject.Find("Enemy"));
                enemy.transform.parent = GameObject.Find("Clones").transform;
                if (i <= 7)
                    enemy.tag = "Enemy";
                else
                    enemy.tag = "SEnemy";
                enemy.layer = LayerMask.NameToLayer("Markierung");
            }
            GameObject spezial = Instantiate(GameObject.Find("Spezial"));
            spezial.transform.parent = GameObject.Find("Clones").transform;
            spezial.tag = "Spezial";
            spezial.layer = LayerMask.NameToLayer("Markierung");
            Free = GameObject.FindGameObjectsWithTag("Free");
            Enemies = GameObject.FindGameObjectsWithTag("Enemy");
            SEnemy = GameObject.FindGameObjectsWithTag("SEnemy");
            Spezial = GameObject.FindGameObjectsWithTag("Spezial");
            Hover = GameObject.Find("Hover");
            Schach = GameObject.FindGameObjectsWithTag("Schach");
            Selection = GameObject.Find("Selection");
            for (int i = 0; i < Board.wFiguren.Count; i++)
            {
                for (int j = 0; j < Board.sFiguren.Count; j++)
                {
                    Physics.IgnoreCollision(Board.wFiguren[i].Objekt.GetComponent<Collider>(), Board.sFiguren[j].Objekt.GetComponent<Collider>());
                }
            }
            instance = this;
        }

        // Update is called once per frame
        public IEnumerator Sleep(float secs)
        {
            yield return new WaitForSeconds(secs);
            for (int i = 0; i < Board.wFiguren.Count; i++)
            {
                for (int j = 0; j < Board.sFiguren.Count; j++)
                {
                    Physics.IgnoreCollision(Board.wFiguren[i].Objekt.GetComponent<Collider>(), Board.sFiguren[j].Objekt.GetComponent<Collider>());
                }
            }
            for (int i = 0; i < Board.sFiguren.Count; i++)
            {
                Board.sFiguren[i].Objekt.GetComponent<Rigidbody>().isKinematic = false;
                if (Board.sFiguren[i].Objekt.tag == "MadeTurn")
                    Board.sFiguren[i].Objekt.tag = "Untagged";
            }
            for (int i = 0; i < Board.wFiguren.Count; i++)
            {
                Board.wFiguren[i].Objekt.GetComponent<Rigidbody>().isKinematic = false;
                if (Board.wFiguren[i].Objekt.tag == "MadeTurn")
                    Board.wFiguren[i].Objekt.tag = "Untagged";
            }
            if (!new KI.State(null, null, 0, 0, Board.Schachfeld, Board.wFiguren, Board.sFiguren).isFinalState() && Board.activeTurn == 1)
                Board.P2.assignedKI.MakeTurn();
        }
        void SwitchTurns()
        {
            Board.activeTurn = Board.activeTurn * -1 + 1;
            StartCoroutine(Sleep(1));
        }
        static bool animates = false;
        void Update()
        {
            // Maus-Events
            // Maus-Klick auf Objekt
            if (Board != null && Board.activeTurn == 0 && !animates)
            {
                PFigur König = Board.wFiguren.Find(x => x.Klasse == PFigur.Typ.König);
                for (int i = 0; i < Board.sFiguren.Count; i++)
                {
                    Board.sFiguren[i].Movement = Spieler.OnSelect(Board.sFiguren[i], Board.Schachfeld, false);
                }
                List<PFigur> threat = KI.FigursAsTarget(König.position, König.Color, Board.sFiguren);
                if (threat.Count > 0 && threat[0].Objekt != null)
                {
                    Schach[0].transform.localPosition = new Vector3(König.Objekt.transform.localPosition.x, -24.96f, König.Objekt.transform.localPosition.z);
                    Schach[1].transform.position = new Vector3(threat[0].Objekt.transform.position.x, 75.04f, threat[0].Objekt.transform.position.z);
                }
                if (Input.GetMouseButtonUp(0) && CameraMain.enabled == true)
                {
                    RaycastHit hit;
                    Ray ray = CameraMain.ScreenPointToRay(Input.mousePosition);
                    if (Physics.Raycast(ray, out hit, 5000.0f, LayerMask.GetMask("Hover")))
                    {
                        int x = -(int)(hit.collider.gameObject.transform.localPosition.z / 11.25f), y = 7 - (int)(hit.collider.gameObject.transform.localPosition.x / 11.25f);
                        int value = Board.Schachfeld[y][x];
                        if (value != 0 && value < 10)
                            value = Board.wFiguren.FindIndex(delegate (PFigur figur) { return (figur.position.x == x && figur.position.y == y); });
                        else value = -1;
                        if (Board.P1.Selected == -1 || value != Board.P1.Selected && value != -1)
                        {
                            Board.P1.ClearSelection();
                            Board.P1.Selected = Board.wFiguren.FindIndex(delegate (PFigur figur) { return (figur.position.x == x && figur.position.y == y); });
                            PlayerPrefs.SetInt("SelPosX", x);
                            PlayerPrefs.SetInt("SelPosY", y);
                            GameObject[] temp = GameObject.FindGameObjectsWithTag("Destroy");
                            for (int i = 0; i < temp.Length; i++)
                            {
                                Destroy(temp[i]);
                            }
                            if (Board.P1.Selected >= 0)
                            {
                                for (int i = 0; i < Board.wFiguren[Board.P1.Selected].Objekt.GetComponentsInChildren<MeshRenderer>().Length; i++)
                                {
                                    Board.wFiguren[Board.P1.Selected].Objekt.GetComponentsInChildren<MeshRenderer>()[i].materials = new Material[2] { SelectedMat, SelectedMat };
                                }
                                GameObject.Find("Selection").transform.localPosition = new Vector3(Board.wFiguren[Board.P1.Selected].Objekt.transform.localPosition.x, -24.96f, Board.wFiguren[Board.P1.Selected].Objekt.transform.localPosition.z);
                                LastHoverMat = SelectedMat;
                            }
                        }
                        else if (Board.P1.Selected >= 0 && !new int[] { 0, 5, 6 }.Contains(Board.wFiguren[Board.P1.Selected].Movement[y, x]))
                        {
                            PFigur.MakeTurn(new KI.Schachzug(Board.wFiguren[Board.P1.Selected], Board.wFiguren[Board.P1.Selected].position, new PFigur.Position(x, y), Board.wFiguren[Board.P1.Selected].Movement[y, x]), 0);
                        }
                    }
                    else
                    {
                        Board.P1.ClearSelection();
                        for (int i = 0; i < Board.wFiguren.Count; i++)
                        {
                            Board.wFiguren[i].Objekt.transform.localPosition = new Vector3((7 - Board.wFiguren[i].position.y) * 11.25f, Board.wFiguren[i].startTransform.localPosition.y, Board.wFiguren[i].position.x * -11.25f);
                            Board.wFiguren[i].Objekt.transform.rotation = Board.wFiguren[i].startTransform.rotation;
                        }
                        for (int i = 0; i < Board.sFiguren.Count; i++)
                        {
                            Board.sFiguren[i].Objekt.transform.localPosition = new Vector3((-Board.sFiguren[i].position.y) * 11.25f, Board.sFiguren[i].startTransform.localPosition.y, Board.sFiguren[i].position.x * -11.25f);
                            Board.sFiguren[i].Objekt.transform.rotation = Board.sFiguren[i].startTransform.rotation;
                        }
                    }
                }
                else if (CameraMain.enabled && !Input.GetMouseButton(0))
                {
                    RaycastHit hit;
                    Ray ray = CameraMain.ScreenPointToRay(Input.mousePosition);
                    if (Physics.Raycast(ray, out hit, 5000.0f, LayerMask.GetMask("Schachbrett")))
                    {
                        if (LastHover != null)
                        {
                            foreach (MeshRenderer renderer in LastHover.GetComponentsInChildren<MeshRenderer>())
                            {
                                renderer.materials = new Material[2] { LastHoverMat, LastHoverMat };
                            }
                            LastHover = null;
                            LastHoverMat = null;
                        }
                        Hover.transform.position = hit.point;
                        Hover.transform.localPosition = new Vector3((((int)((Hover.transform.localPosition.x + 5.625f) / 11.25f))) * 11.25f, -24.90f,
                            ((int)((Hover.transform.localPosition.z - 5.625f) / 11.25f)) * 11.25f);
                        int x = -(int)(Hover.transform.localPosition.z / 11.25f), y = 7 - (int)(Hover.transform.localPosition.x / 11.25f);
                        GameObject objekt;
                        if (Board.Schachfeld[y][x] != 0)
                        {
                            if (Board.Schachfeld[y][x] > 9)
                                objekt = Board.sFiguren.Find(f => f.position == new PFigur.Position(x, y)).Objekt;
                            else
                                objekt = Board.wFiguren.Find(f => f.position == new PFigur.Position(x, y)).Objekt;
                            LastHover = objekt;
                            foreach (MeshRenderer renderer in objekt.GetComponentsInChildren<MeshRenderer>())
                            {
                                LastHoverMat = renderer.materials[0];
                                renderer.materials = new Material[2] { HoverMat, HoverMat };
                            }
                        }
                    }
                    else
                    {
                        Hover.gameObject.transform.position = new Vector3(0, -900, 0);
                        if (LastHover != null)
                        {
                            foreach (MeshRenderer renderer in LastHover.GetComponentsInChildren<MeshRenderer>())
                            {
                                renderer.materials = new Material[2] { LastHoverMat, LastHoverMat };
                            }
                            LastHover = null;
                            LastHoverMat = null;
                        }
                    }
                }
                else if (CameraMain.enabled == false && CameraTransform.enabled == true && Input.GetMouseButton(0))
                {
                    RaycastHit hit;
                    Ray ray = CameraTransform.ScreenPointToRay(Input.mousePosition);
                    PlayerPrefs.SetString("TransformTarget", "");
                    GameObject[] temp = GameObject.FindGameObjectsWithTag("Transformation");
                    for (int i = 0; i < temp.Length; i++)
                    {
                        if (temp[i].gameObject.GetComponent<MeshRenderer>() != null)
                            temp[i].gameObject.GetComponent<MeshRenderer>().material = WeißMat;
                        else
                        {
                            for (int j = 0; j < temp[i].gameObject.GetComponentsInChildren<MeshRenderer>().Length; j++)
                            {
                                temp[i].gameObject.GetComponentsInChildren<MeshRenderer>()[j].materials = new Material[2] { WeißMat, WeißMat };
                            }
                        }
                    }
                    if (Physics.Raycast(ray, out hit, 5000.0f) && hit.collider.gameObject.tag == "Transformation")
                    {
                        PlayerPrefs.SetString("TransformTarget", hit.collider.gameObject.name);
                        if (hit.collider.gameObject.GetComponent<MeshRenderer>() != null)
                            hit.collider.gameObject.GetComponent<MeshRenderer>().material = SelectedMat;
                        else
                        {
                            for (int i = 0; i < hit.collider.gameObject.GetComponentsInChildren<MeshRenderer>().Length; i++)
                            {
                                hit.collider.gameObject.GetComponentsInChildren<MeshRenderer>()[i].materials = new Material[2] { SelectedMat, SelectedMat };
                            }
                        }
                    }
                }
                if (Input.GetKeyUp(KeyCode.Return))
                {
                    switch (PlayerPrefs.GetString("TransformTarget"))
                    {
                        case "Turm":
                            TransformPawn("Turm", PFigur.Typ.Turm);
                            break;
                        case "Läufer":
                            TransformPawn("Läufer", PFigur.Typ.Läufer);
                            break;
                        case "Springer":
                            TransformPawn("Springer", PFigur.Typ.Springer);
                            break;
                        case "Königin":
                            TransformPawn("Königin", PFigur.Typ.Dame);
                            break;
                        default:
                            break;
                    }
                    PlayerPrefs.SetString("TransformTarget", "");
                    GameObject[] temp = GameObject.FindGameObjectsWithTag("Transformation");
                    for (int i = 0; i < temp.Length; i++)
                    {
                        if (temp[i].gameObject.GetComponent<MeshRenderer>() != null)
                            temp[i].gameObject.GetComponent<MeshRenderer>().material = WeißMat;
                        else
                        {
                            for (int j = 0; j < temp[i].gameObject.GetComponentsInChildren<MeshRenderer>().Length; j++)
                            {
                                temp[i].gameObject.GetComponentsInChildren<MeshRenderer>()[j].materials = new Material[2] { WeißMat, WeißMat };
                            }
                        }
                    }
                }
            }
        }
        void TransformPawn(string name, PFigur.Typ typ)
        {
            int selected = Board.wFiguren.FindIndex(x => x.position == toTransform.position);
            CameraTransform.enabled = false;
            CameraTransform.GetComponent<AudioListener>().enabled = false;
            CameraMain.enabled = true;
            CameraMain.GetComponent<AudioListener>().enabled = true;
            Destroy(Board.wFiguren[selected].Objekt);
            Board.wFiguren[selected] = new PFigur(typ, Board.wFiguren[selected].position, Board.wFiguren[selected].Color);
            Board.wFiguren[selected].Objekt = Instantiate(GameObject.Find(name));
            Board.wFiguren[selected].Objekt.transform.parent = GameObject.Find("White").transform;
            Board.wFiguren[selected].Objekt.transform.localPosition = new Vector3((7 - Board.wFiguren[selected].position.y) * 11.25f, -22.4f, Board.wFiguren[selected].position.x * -11.25f);
            Board.wFiguren[selected].Objekt.layer = LayerMask.NameToLayer("FigurW");
            Board.wFiguren[selected].Objekt.tag = "Untagged";
            Board.wFiguren[selected].Objekt.AddComponent<FigurCollision>();
            Board.Schachfeld[Board.wFiguren[selected].position.y][Board.wFiguren[selected].position.x] = Board.wFiguren[selected].ToClassIndex();
            Board.P1.ClearSelection();
            SwitchTurns();
        }
        public static Vector2 scrollPos = Vector2.zero;
        private readonly bool inCheck;
        public float maxZVWidth = 0f;
        public void OnGUI()
        {
            GUI.enabled = true;
            GUI.skin = guiskin;
            GUI.Box(new Rect(Screen.width - 300, 0, 300, 300), "");
            if (Zugverlauf.Count > 0 && GUI.skin.label.CalcSize(new GUIContent(Zugverlauf[Zugverlauf.Count - 1].ToString())).x > maxZVWidth)
            {
                maxZVWidth = GUI.skin.label.CalcSize(new GUIContent(Zugverlauf[Zugverlauf.Count - 1].ToString())).x;
            }
            scrollPos = GUI.BeginScrollView(new Rect(Screen.width - 300, 0, 300, 300), scrollPos, new Rect(0, 0, maxZVWidth + 10, Zugverlauf.Count * 22));
            {
                for (int i = 0; i < Zugverlauf.Count; i++)
                {
                    GUI.skin.GetStyle("label").normal.textColor = new Color((float)Math.Pow(-1, (float)Zugverlauf[i].Figur.Color + 1), 0, (float)Math.Pow(-1, (float)Zugverlauf[i].Figur.Color));
                    GUI.Label(new Rect(0, 22 * i, GUI.skin.label.CalcSize(new GUIContent(Zugverlauf[Zugverlauf.Count - 1].ToString())).x + 10, 22), Zugverlauf[i].ToString());

                }
            }
            GUI.EndScrollView();
            if (GUI.Button(new Rect(Screen.width - 200, 330, 100, 30), "Log Generieren"))
            {
                if (!Directory.Exists("Logs"))
                    Directory.CreateDirectory("Logs");
                int i = 0;
                while (File.Exists("Logs\\Log_" + i.ToString() + ".txt"))
                    i++;
                StreamWriter sw1 = new StreamWriter("Logs\\Log_" + (i).ToString() + ".txt");
                for (i = 0; i < Zugverlauf.Count; i++)
                    sw1.WriteLine(Zugverlauf[i]);
                sw1.Close();
            }
            if (CameraTransform.enabled)
            {
                GUI.skin.box.alignment = TextAnchor.MiddleCenter;
                GUI.skin.box.fontStyle = FontStyle.Bold;
                GUI.Box(new Rect((Screen.width / 2f) - 150, Screen.height - 65, 300, 30), "Zum Bestätigen, bitte 'Enter' drücken!");
            }
        }
    }
}