using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

public class TetrahedronModule : MonoBehaviour {
	private const float EDGE_LENGTH = 0.0643f;
	private const int PER_STAGE_INITIAL_REWARD = 20;
	private const int PER_STAGE_REWARD_MULTIPLIER = 4;
	private const int OTHER_VALID_TETRAHEDRON_MULTIPLIER = 50;

	private static readonly Vector3[] BASE_NODES_POSITIONS = new Vector3[] {
		new Vector3(0, 0.0275f, 0.06f),
		new Vector3(-0.0693f, 0.0275f, -0.06f),
		new Vector3(0.0693f, 0.0275f, -0.06f),
	};

	private static int moduleIdCounter = 1;

	public readonly string TwitchHelpMessage = "\"!{0} ABCD\" [Submit a path of selected vertices A,B,C,D, in that order. Refer to the manual of the vertices on the module.]";

	public bool TwitchPlaysActive;
	public TextMesh Display;
	public KMSelectable Selectable;
	public KMBombModule Module;
	public KMBossModule BossModule;
	public KMBombInfo BombInfo;
	public KMAudio Audio;
	public NodeComponent NodePrefab;
	public EdgeComponent EdgePrefab;

	private bool forceSolved = false;
	private bool activated = false;
	private bool stageActive;
	private bool playActivationSound;
	private int moduleId;
	private int startingTimeInMinutes;
	private int passedStagesCount = 0;
	private int stagesCount;
	private int pathLength;
	private int registeredSolvesCount = 0;
	private int score = 0;
	private string currentStagePathExample = null;
	private string currentPath;
	private HashSet<string> pathesExamples;
	private HashSet<string> usedPathes = new HashSet<string>();
	private HashSet<TetrahedronModule> otherTetrahedrons;
	private NodeComponent[] baseNodes;
	private NodeComponent startNode;
	private EdgeComponent[] baseEdges = new EdgeComponent[3];
	private EdgeComponent[] startEdges = new EdgeComponent[3];
	private HashSet<string> ignoredModules;

	private NodeComponent GetNodeById(char id) {
		return id == 'd' ? startNode : baseNodes[id - 'a'];
	}

	private void Start() {
		moduleId = moduleIdCounter++;
		baseNodes = BASE_NODES_POSITIONS.Select(pos => InstantiateNode(pos)).ToArray();
		startNode = InstantiateNode(new Vector3(0, 0.1075f, -0.02f));
		for (int i = 0; i < baseNodes.Length; i++) {
			NodeComponent from = baseNodes[i];
			NodeComponent to = baseNodes[(i + 1) % baseNodes.Length];
			EdgeComponent edge = Instantiate(EdgePrefab);
			edge.transform.parent = transform;
			edge.transform.localPosition = (from.transform.localPosition + to.transform.localPosition) / 2f;
			edge.transform.localScale = new Vector3(.01f, EDGE_LENGTH, .01f);
			edge.transform.localRotation = Quaternion.LookRotation(to.transform.localPosition - from.transform.localPosition, Vector3.up);
			edge.transform.Rotate(90, 0, 0, Space.Self);
			edge.Id = string.Format("{0}{1}", (char)('a' + i), (char)('a' + (i + 1) % 3));
			baseEdges[i] = edge;
			edge = Instantiate(EdgePrefab);
			edge.transform.parent = transform;
			edge.transform.localPosition = (from.transform.localPosition + startNode.transform.localPosition) / 2f;
			edge.transform.localScale = new Vector3(.01f, EDGE_LENGTH, .01f);
			edge.transform.localRotation = Quaternion.LookRotation(startNode.transform.localPosition - from.transform.localPosition, Vector3.up);
			edge.transform.Rotate(90, 0, 0, Space.Self);
			edge.Id = "d" + (char)('a' + i);
			startEdges[i] = edge;
		}
		Module.OnActivate += ActivateModule;
	}

	private void ActivateModule() {
		activated = true;
		startingTimeInMinutes = Mathf.FloorToInt(BombInfo.GetTime() / 60f);
		Debug.LogFormat("[Tetrahedron #{0}] Starting time in minutes: {1}", moduleId, startingTimeInMinutes);
		int modulesCount = transform.parent.childCount;
		otherTetrahedrons = new HashSet<TetrahedronModule>(Enumerable.Range(0, modulesCount).Select(i => (
			transform.parent.GetChild(i).GetComponent<TetrahedronModule>()
		)).Where(m => m != null && m != this));
		playActivationSound = otherTetrahedrons.All(t => t.otherTetrahedrons == null);
		ignoredModules = new HashSet<string>(BossModule.GetIgnoredModules("Tetrahedron", TetrahedronData.DefaultIgnoredModules));
		List<string> allModules = BombInfo.GetSolvableModuleNames();
		int unignoredModulesCount = allModules.Where(m => !ignoredModules.Contains(m)).Count();
		int tetrahedronModulesCount = allModules.Count(s => s == "Tetrahedron");
		stagesCount = unignoredModulesCount + 1;
		Debug.LogFormat("[Tetrahedron #{0}] Stages count: {1}", moduleId, stagesCount);
		int expectedPathesCount = stagesCount * tetrahedronModulesCount;
		int combinationsCount;
		pathesExamples = TetrahedronData.Generate(expectedPathesCount, out combinationsCount);
		pathLength = pathesExamples.PickRandom().Length;
		Debug.LogFormat("[Tetrahedron #{0}] Path length: {1} ({2} combinations!)", moduleId, pathLength, combinationsCount);
		for (int i = 0; i < baseNodes.Length; i++) {
			int i_ = i;
			baseNodes[i].Selectable.OnInteract += () => { PressNode((char)('a' + i_)); return false; };
		}
		startNode.Selectable.OnInteract += () => { PressNode('d'); return false; };
		ActivateStage();
	}

	private void Update() {
		if (!activated) return;
		int solvedUnignoredModulesCount = BombInfo.GetSolvedModuleNames().Where(s => !ignoredModules.Contains(s)).Count();
		int newSolves = solvedUnignoredModulesCount - registeredSolvesCount;
		if (newSolves == 0) return;
		if (stageActive) {
			for (int i = 0; i < newSolves; i++) {
				if (!forceSolved) {
					Debug.LogFormat("[Tetrahedron #{0}] Other module is solved before input. Strike!", moduleId);
					Module.HandleStrike();
				}
				if (playActivationSound && otherTetrahedrons.Any(t => t.passedStagesCount == solvedUnignoredModulesCount)) {
					Audio.PlaySoundAtTransform("TetrahedronActivated", transform);
				}
			}
		} else ActivateStage();
		registeredSolvesCount = solvedUnignoredModulesCount;
	}

	private void ActivateStage(bool reset = false) {
		if (stageActive) return;
		if (playActivationSound && !reset) Audio.PlaySoundAtTransform("TetrahedronActivated", startNode.transform);
		HashSet<string> allUsedPathes = new HashSet<string>(usedPathes.Concat(otherTetrahedrons.SelectMany(t => t.usedPathes)).Concat(otherTetrahedrons.Select(t => (
			t.currentStagePathExample
		))));
		string possiblePath = pathesExamples.Where(p => !allUsedPathes.Contains(p)).PickRandom();
		currentStagePathExample = possiblePath;
		// Debug.Log(possiblePath);
		HashSet<EdgeComponent> usedEdges = new HashSet<EdgeComponent>(PathToEdges(possiblePath));
		HashSet<Color> passableColors = new HashSet<Color>();
		HashSet<Color> impassableColors = new HashSet<Color>();
		foreach (Color color in TetrahedronData.COLORS) {
			if (TetrahedronData.IsPassable(color, BombInfo, startingTimeInMinutes)) passableColors.Add(color);
			else impassableColors.Add(color);
		}
		if (passedStagesCount == 0 && !reset) {
			Debug.LogFormat("[Tetrahedron #{0}] Passable colors: {1}", moduleId, passableColors.Select(c => TetrahedronData.colorNames[c]).Join(","));
			Debug.LogFormat("[Tetrahedron #{0}] Impassable colors: {1}", moduleId, impassableColors.Select(c => TetrahedronData.colorNames[c]).Join(","));
		}
		Debug.LogFormat("[Tetrahedron #{0}] Stage #{1}:", moduleId, passedStagesCount + 1);
		foreach (EdgeComponent edge in baseEdges.Concat(startEdges)) {
			edge.color = usedEdges.Contains(edge) ? passableColors.PickRandom() : impassableColors.PickRandom();
			Debug.LogFormat("[Tetrahedron #{0}] Edge \"{1}\" is {2}", moduleId, edge.Id.ToUpper(), TetrahedronData.colorNames[edge.color.Value]);
		}
		Debug.LogFormat("[Tetrahedron #{0}] Possible path: {1}", moduleId, possiblePath.ToUpper());
		stageActive = true;
		startNode.currentPosition = true;
		currentPath = "";
		UpdateDisplay();
	}

	private void PressNode(char nodeId) {
		if (!stageActive) return;
		char currentNodeId = currentPath.Length == 0 ? 'd' : currentPath.Last();
		if (currentNodeId == nodeId) return;
		GetNodeById(currentNodeId).currentPosition = false;
		currentPath += nodeId;
		EdgeComponent edge = MoveToEdge(currentNodeId, nodeId);
		if (!TetrahedronData.IsPassable(edge.color.Value, BombInfo, startingTimeInMinutes)) {
			Debug.LogFormat("[Tetrahedron #{0}] Trying to use edge \"{1}\" that is {2}. Strike!", moduleId, edge.Id.ToUpper(), TetrahedronData.colorNames[edge.color.Value]);
			if (!forceSolved)
				Module.HandleStrike();
			currentPath = "";
			startNode.currentPosition = true;
		} else if (currentPath.Length == pathLength) {
			Debug.LogFormat("[Tetrahedron #{0}] Submitted path: {1}", moduleId, currentPath.ToUpper());
			bool pathIsValid = true;
			bool shouldReset = false;
			if (usedPathes.Contains(currentPath)) {
				Debug.LogFormat("[Tetrahedron #{0}] Submitted path was used before. Strike!", moduleId);
				pathIsValid = false;
			} else if (currentPath.Last() != 'd') {
				Debug.LogFormat("[Tetrahedron #{0}] Submitted path not ends at \"D\" node. Strike!", moduleId);
				pathIsValid = false;
			} else if (!TwitchPlaysActive && otherTetrahedrons.Any(t => t.usedPathes.Contains(currentPath))) {
				Debug.LogFormat("[Tetrahedron #{0}] Submitted path was already used in other Tetrahedron. Strike and reset!", moduleId);
				pathIsValid = false;
				shouldReset = true;
			}
			if (pathIsValid) {
				Debug.LogFormat("[Tetrahedron #{0}] Submitted path is correct", moduleId);
				usedPathes.Add(currentPath);
				currentPath = "";
				passedStagesCount++;
				if (passedStagesCount == stagesCount) {
					Audio.PlayGameSoundAtTransform(KMSoundOverride.SoundEffect.CorrectChime, transform);
					Debug.LogFormat("[Tetrahedron #{0}] No more stages. Module solved", moduleId);
					Module.HandlePass();
				} else Audio.PlaySoundAtTransform("TetrahedronStagePassed", startNode.transform);
				stageActive = false;
				foreach (EdgeComponent e in baseEdges.Concat(startEdges)) e.color = null;
				if (registeredSolvesCount >= passedStagesCount) ActivateStage();
			} else {
				if (!forceSolved)
				Module.HandleStrike();
				if (shouldReset) {
					stageActive = false;
					ActivateStage(true);
				} else {
					currentPath = "";
					startNode.currentPosition = true;
				}
			}
		} else {
			NodeComponent pressedNode = GetNodeById(nodeId);
			pressedNode.currentPosition = true;
			Audio.PlaySoundAtTransform("TetrahedronNodePressed", pressedNode.transform);
		}
		UpdateDisplay();
	}

	public IEnumerator ProcessTwitchCommand(string command) {
		command = command.Trim().ToLower();
		if (!Regex.IsMatch(command, "^[a-d]{2,}$")) yield break;
		for (int i = 1; i < command.Length; i++) {
			if (command[i] == command[i - 1]) {
				yield return "sendtochat {0}, !{1} duplicates found";
				yield break;
			}
		}
		if (command[0] == 'd') {
			yield return "sendtochat {0}, !{1} first move is invalid";
			yield break;
		}
		yield return null;
		if (command.Length != pathLength) {
			yield return "sendtochat {0}, !{1} requires path with length " + pathLength.ToString();
			yield break;
		}
		if (!stageActive) {
			yield return "sendtochat {0}, !{1} Tetrahedron not active";
			yield break;
		}
		int prevStages = passedStagesCount;
		foreach (char c in command) {
			PressNode(c);
			if (currentPath.Length == 0) break;
			yield return new WaitForSeconds(.1f);
		}
		if (prevStages == passedStagesCount) yield break;
		int addScores = PER_STAGE_INITIAL_REWARD + prevStages * PER_STAGE_REWARD_MULTIPLIER;
		Debug.LogFormat("[Tetrahedron #{0}] Reward points: {1}.{2}", moduleId, addScores / 100, (addScores % 100).ToString().PadLeft(2, '0'));
		score += addScores;
		if (passedStagesCount == stagesCount && otherTetrahedrons.Count > 0) {
			KeyValuePair<string, TetrahedronModule>[] otherUsages = usedPathes.SelectMany(p => otherTetrahedrons.Select(t => (
				new KeyValuePair<string, TetrahedronModule>(p, t)
			))).Where(pair => pair.Value.usedPathes.Contains(pair.Key)).ToArray();
			if (otherUsages.Length == 0) {
				int addPoints = OTHER_VALID_TETRAHEDRON_MULTIPLIER * stagesCount * otherTetrahedrons.Count;
				Debug.LogFormat("[Tetrahedron #{0}] No conflicts with another {1} Tetrahedrons. Reward points: {2}.{3}", moduleId, otherTetrahedrons.Count, addPoints / 100,
					(addPoints % 100).ToString().PadLeft(2, '0'));
				score += addPoints;
			} else {
				KeyValuePair<string, TetrahedronModule> pair = otherUsages.PickRandom();
				Debug.LogFormat("[Tetrahedron #{0}] Path \"{1}\" was used at Tetrahedron #{2}. No additional reward points", moduleId, pair.Key.ToUpper(),
					pair.Value.moduleId);
			}
		}
		int newScores = score / 100;
		if (newScores == 0) yield break;
		string award = "awardpoints " + newScores.ToString();
		Debug.LogFormat("[Tetrahedron #{0}] Run twitch {1}", moduleId, award);
		score %= 100;
		yield return award;
	}

	private void TwitchHandleForcedSolve()
    {
		forceSolved = true;
		StartCoroutine(StartTetrahedronAutosolver());
    }

	private IEnumerator StartTetrahedronAutosolver() {
		
		Debug.LogFormat("[Tetrahedron #{0}] Autosolver started", moduleId);
		yield return new WaitForSeconds(.1f);
		while (!activated) yield return true;
		while (passedStagesCount != stagesCount) {
			while (!stageActive) yield return true;
			char currentNodeId = currentPath.Length == 0 ? 'd' : currentPath.Last();
			if (currentNodeId != 'd') {
				GetNodeById(currentNodeId).currentPosition = false;
				startNode.currentPosition = true;
			}
			currentPath = "";
			foreach (char c in currentStagePathExample) {
				PressNode(c);
				yield return new WaitForSeconds(.1f);
			}
		}
	}

	private NodeComponent InstantiateNode(Vector3 localPosition) {
		NodeComponent node = Instantiate(NodePrefab);
		node.transform.parent = transform;
		node.transform.localPosition = localPosition;
		node.transform.localScale = Vector3.one;
		node.transform.localRotation = Quaternion.identity;
		node.Selectable.Parent = Selectable;
		Selectable.Children = Selectable.Children.Concat(new[] { node.Selectable }).ToArray();
		Selectable.UpdateChildren();
		return node;
	}

	private void UpdateDisplay() {
		Display.text = string.Format("{0} / {1}", stageActive ? currentPath.Length.ToString() : "-", pathLength);
	}

	private EdgeComponent[] PathToEdges(string path) {
		char prevPos = 'd';
		return path.Select(c => {
			EdgeComponent result = MoveToEdge(prevPos, c);
			prevPos = c;
			return result;
		}).ToArray();
	}

	private EdgeComponent MoveToEdge(char from, char to) {
		if (from == 'd') return startEdges[to - 'a'];
		if (to == 'd') return startEdges[from - 'a'];
		char[] sorted = new[] { from, to };
		Array.Sort(sorted);
		if (sorted[0] == 'a') return sorted[1] == 'b' ? baseEdges[0] : baseEdges[2];
		return baseEdges[1];
	}
}
