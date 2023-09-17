using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Nodes.Enums;
using UnityEngine;
using Utils;

public class GraphManager : MonoBehaviour
{
    public GameObject cubeNode;
    public GameObject mainMenu;

    private static GraphManager singltone;
    public SettingsParams settingsParams;
    private List<List<Node>> parts = new List<List<Node>>();
    private Volume volume;
    public Material lineMaterial;
    public LineArrow lr;
    public List<GameObject> LineObjectList;
    
    
    private readonly List<NodeView> _views = new List<NodeView>(); 

    private void Awake() {
        settingsParams = GetComponent<SettingsParams>();
        singltone = this;
    }

    public static GraphManager Get()
    {
        if (singltone is null)
        {
            var gameObject = GameObject.Find("GRAPH_MANAGER");
            singltone = gameObject.GetComponentInChildren<GraphManager>();
        }
        return singltone;
    }

    public async Task Init(List<Node> sceneNodes, bool centerCamera = true, bool normalizeDistance = true)
    {
        Camera.main.backgroundColor = settingsParams.BgColor;

        Clear();
        
        volume = new Volume();
        parts = Node.FindIsolatedGraphs(sceneNodes);
        for (var i = 0; i < parts.Count; i++)
        {
            var nodes = parts[i];
            Node.AlignNodes(nodes);
            await Node.AlignNodesByForceDirected(nodes);
            CreateGameObjectsFromNodes(nodes, i, volume);
        }

        if (!centerCamera && !normalizeDistance) return;
        
        var orbit = Camera.main.GetComponent<DragMouseOrbit>();
        if (centerCamera)
        {
            volume.CenterCamera(orbit);
        }

        if (normalizeDistance)
        {
            volume.NormalizeDistance(orbit);   
        }
    }

    public IEnumerable<Node> GetNodes()
    {
        var nodes = parts.SelectMany(value => value);
        return nodes;
    }

    public void Clear()
    {
        ClickNode.instance.node = null;
        
        _views.ForEach(value => Destroy(value.gameObject));
        _views.Clear();
        
        parts.Clear();
    }
    
    public List<List<Node>> GetParts()
    {
        return parts;
    }

    private void CreateGameObjectsFromNodes(List<Node> nodes, int offset, Volume volume)
    {
        var definitions = new Dictionary<Node, GameObject>();
        var links = new HashSet<(Node from, Node to)>();
        foreach (var node in nodes)
        {
            var position = (node.position + new Vector3(0, 0, offset)) * 2;
            position.y = -position.y;
            
            node.gameObject = Instantiate(cubeNode, position, Quaternion.identity);
            node.gameObject.GetComponent<MeshRenderer>().material.color = settingsParams.nodeColor;
            
            
            var view = node.gameObject.GetComponent<NodeView>();
            view.nodeLink = node;
            _views.Add(view);
            
            volume.Add(node.gameObject);
            view.SetText(node.text);
            definitions[node] = node.gameObject;
            
            foreach (var input_node in node.inputs)
                links.Add((from: input_node, to: node));
            foreach (var output_node in node.outputs)
                links.Add((from: node, to: output_node));
        }
        foreach (var (from, to) in links)
            LineTo(definitions[from], definitions[to]);
    }

    public async Task LinkNode(Node node, Node target, List<Node> graph, ENodeForce force)
    {
        if (force == ENodeForce.Out)
        {
            target.trueOutputs.Add(node);
            node.inputs.Add(target);
        }
        else
        {
            target.inputs.Add(node);
            node.trueOutputs.Add(target);
        }
        
        graph.Add(node);

        await Node.AlignNodesByForceDirected(graph);

        node.gameObject = Instantiate(cubeNode);
        var view = node.gameObject.GetComponent<NodeView>();
        view.nodeLink = node;
        view.SetText(node.text);
        _views.Add(view);

        var all = parts.SelectMany(value => value).ToList();
        ReDraw(all);
        AlignByGraph(all);
    }

    public async Task RemoveNode(Node node, List<Node> graph)
    {
        if (!Validate()) return;
        
        node.ClearAllRelationships(graph);
        graph.Remove(node);

        var view = node.gameObject.GetComponent<NodeView>();
        _views.Remove(view);
        
        Destroy(view.gameObject);
        
        parts = Node.FindIsolatedGraphs(graph);
        await Node.AlignNodesByForceDirected(graph);

        ReDraw(graph);
        AlignByGraph(graph);

        bool Validate()
        {
            var count = 0;
            
            if (node.inputs.Any()) count++;
            if (node.trueOutputs.Any()) count++;

            return count <= 1;
        }
    }

    private static void AlignByGraph(List<Node> graph)
    {
        var volume = new Volume();
        foreach (var node in graph)
        {
            volume.Add(node.gameObject);
        }

        var orbit = Camera.main.GetComponent<DragMouseOrbit>();
        
        volume.CenterCamera(orbit);
        volume.NormalizeDistance(orbit);
    }

    public void LineTo(GameObject start, GameObject stop) 
    {
        var LineObject = new GameObject();
        if(LineObjectList != null || LineObjectList[LineObjectList.Count-1].GetComponent<LineRenderer>().positionCount == 2)
        {
            LineObject = new GameObject();
            LineObject.transform.SetParent(start.transform);
            LineObject.AddComponent<LineRenderer>();
            LineObjectList.Add(LineObject);
        }
        var lineRend = LineObject.GetComponent<LineRenderer>();
        lineRend.material = lineMaterial;
        lineRend.material.color = settingsParams.lineColor;
        lineRend.widthMultiplier = 0.1f;
        lineRend.positionCount = 0;
        
        //set line and instance arrow
        var _start = GameObject.CreatePrimitive(PrimitiveType.Cube);
        _start.transform.SetParent(start.transform);
        _start.transform.localPosition = Vector3.zero;
        _start.transform.LookAt(stop.transform);
        _start.transform.Translate(Vector3.forward*0.8f, Space.Self);
        lineRend.positionCount += 2;
        lineRend.SetPosition(lineRend.positionCount - 2, _start.transform.position);
        Destroy(_start);
        var _stop = GameObject.CreatePrimitive(PrimitiveType.Cube);
        _stop.transform.SetParent(stop.transform);
        _stop.transform.localPosition = Vector3.zero;
        _stop.transform.LookAt(start.transform);
        _stop.transform.Translate(Vector3.forward*0.8f, Space.Self);
        lineRend.SetPosition(lineRend.positionCount - 1, _stop.transform.position);   
        lr.ArrowPoint(stop.gameObject, _stop.gameObject, start.gameObject);
        lr.arrow.GetComponentInChildren<MeshRenderer>().sharedMaterial.color = settingsParams.lineColor;
        Destroy(_stop);
    }

    private void ReDraw(List<Node> graph)
    {
        // clear links

        foreach (var n in graph)
        {
            NodeView nodeView = n.gameObject.GetComponent<NodeView>();
            if (n.gameObject.TryGetComponent<LineRenderer>(out var lineRenderer))
            {
                lineRenderer.positionCount = 0;
            }

            for (int i = 0; i < LineObjectList.Count; i++)
            {
                Destroy(LineObjectList[i]);
            }

            for (int i = 0; i < n.gameObject.transform.childCount; i++)
            {
                if (n.gameObject.transform.GetChild(i) != nodeView.Origin)
                {
                    Destroy(n.gameObject.transform.GetChild(i).gameObject);
                }
            }
        }

        // set positions and links
        var definitions = new Dictionary<Node, GameObject>();
        var links = new HashSet<(Node from, Node to)>();
        foreach (var n in graph)
        {
            var position = n.position * 2;
            position.y = -position.y;
            n.gameObject.transform.position = position;

            definitions[n] = n.gameObject;
            foreach (var input_node in n.inputs)
                links.Add((from: input_node, to: n));
            foreach (var output_node in n.outputs)
                links.Add((from: n, to: output_node));
        }

        foreach (var (from, to) in links)
        {
            if (from == null || to == null)
                continue; 
            
            if (!definitions.ContainsKey(from) || !definitions.ContainsKey(to))
                continue;

            LineTo(definitions[from], definitions[to]);
        }
    }
}

class Volume
{
    public Vector3 Min = new(float.MaxValue, float.MaxValue, float.MaxValue);
    public Vector3 Max = new(float.MinValue, float.MinValue, float.MinValue);

    // Made to give user possibility to can see nodes after create empty graph (and in some other cases) 
    private const float DistanceOffset = 3f;

    public void Add(GameObject gameObject)
    {
        var position = gameObject.transform.position;

        if (Min.x > position.x)
            Min.x = position.x;
        if (Min.y > position.y)
            Min.y = position.y;
        if (Min.z > position.z)
            Min.z = position.z;

        if (Max.x < position.x)
            Max.x = position.x;
        if (Max.y < position.y)
            Max.y = position.y;
        if (Max.z < position.z)
            Max.z = position.z;
    }

    public Vector3 GetCenter()
    {
        return (Max + Min) / 2;
    }

    public float GetRadius()
    {
        return ((Max - Min) / 2).magnitude;
    }

    public void CenterCamera(DragMouseOrbit orbit)
    {
        orbit.target = GetCenter();
    }

    public void NormalizeDistance(DragMouseOrbit orbit)
    {
        orbit.distance = GetRadius() * 2 + DistanceOffset;
    }
}
