using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class ColorButton : MonoBehaviour
{
    public GameObject colorPic;
    public ColorPicker colorPicker;
    public SettingsView settingsView;

    public Color initColor;

    private void OnEnable() {
        GetComponent<Button>().onClick.AddListener(SetButtonClicked);
        GetComponent<Button>().onClick.AddListener(SetInitColor);
    }

    void Update()
    {
        //SetButtonClicked();
    }

    public void SwitchColorPicker()
    {
        colorPic.gameObject.SetActive(!colorPicker.gameObject.activeSelf);
    }

    public void ActiveColorPicker()
    {
        colorPic.gameObject.SetActive(true);
        colorPicker.FadeScreen.SetActive(true);
    }

    public void DisableColorPicker()
    {
        colorPic.gameObject.SetActive(false);
        colorPicker.FadeScreen.SetActive(false);
    }

    public void SetButtonClicked()
    {
        settingsView.buttonClicked = gameObject;
    }

    public void SetInitColor()
    {
        initColor = transform.GetChild(0).GetComponent<Image>().color;
        colorPicker.initColor = initColor;
    }

    
}